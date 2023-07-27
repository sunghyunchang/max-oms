using Akka.Actor;
using System.Buffers;
using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Timers;
using maxoms.models;

namespace maxoms.comm
{
    // Actor Name   : /user/2
    // Description  : New/Cancel Order Request
    internal class OmsTcp2 : UntypedActor
    {
        #region Instance       
        const int PortNO = 2;

        Socket Conn { get; set; }
        DateTime RecvLastTime { get; set; }
        System.Timers.Timer BeatTimer { get; set; }
        #endregion

        #region Main
        public OmsTcp2()
        {
            Connecting();
        }
        #endregion

        #region Connecting TCP(client)
        async void Connecting()
        {
            var ipAddr   = IPAddress.Parse(Sys.IConfDB["Max:Active:Ip"]);
            var port     = Sys.IConfDB[$"Max:Active:Port:2"];
            var endPoint = new IPEndPoint(ipAddr, int.Parse(port));

            Sys.ILog.Information($"Connecting. OMS='{PortNO}', IP='{endPoint.Address}', Port='{endPoint.Port}'");
            Conn = new Socket(ipAddr.AddressFamily, SocketType.Stream, ProtocolType.Tcp);

            try
            {
                Conn.Connect(endPoint);
                await ProcessLinesAsync(Conn);
            }
            catch (Exception ex)
            {
                Sys.ILog.Error($"OMS='{PortNO}', Error='{ex.Message}'");

                Sys.ActSys.WhenTerminated.Wait(3000);
                Connecting();
            }
        }
        #endregion

        #region Create PipeWriter/PipeReader pair.
        async Task ProcessLinesAsync(Socket socket)
        {
            var pipe        = new Pipe();
            Task writing    = FillPipeAsync(socket, pipe.Writer);
            Task reading    = ReadPipeAsync(pipe.Reader);

            await Task.WhenAll(reading, writing);
        }
        #endregion

        #region Receive packet and put in pipeline buffer
        async Task FillPipeAsync(Socket socket, PipeWriter writer)
        {
            const int minimumBufferSize = 1024;

            try
            {
                while (true)
                {
                    // Allocate at least 1024 bytes from the PipeWriter.
                    Memory<byte> memory = writer.GetMemory(minimumBufferSize);
                    try
                    {
                        // receive length
                        int bytesRead = await socket.ReceiveAsync(memory, SocketFlags.None);

                        if (bytesRead == 0) break;

                        // Tell the PipeWriter how much was read from the Socket.
                        writer.Advance(bytesRead);

                        // Make the data available to the PipeReader.
                        FlushResult result = await writer.FlushAsync();

                        if (result.IsCompleted) break;
                    }
                    catch (Exception ex)
                    {
                        Sys.ILog.Error($"OMS='{PortNO}', ErrMsg='{ex.Message}'");
                        break;
                    }
                }
            }
            finally
            {
                // By completing PipeWriter, tell the PipeReader that there's no more data coming.
                await writer.CompleteAsync();
            }
        }
        #endregion

        #region Read in pipeline
        async Task ReadPipeAsync(PipeReader reader)
        {            
            SendAccessToMax();
            SetHeartbeatTimer();

            try
            {
                while (true)
                {
                    ReadResult result = await reader.ReadAsync();
                    ReadOnlySequence<byte> buffer = result.Buffer;

                    try
                    {
                        // The read was canceled. You can quit without reading the existing data.
                        if (result.IsCanceled) break;

                        while (TryReadLine(ref buffer, out ReadOnlySequence<byte> line))
                        {                            
                            Parsing(line);
                        }

                        // Stop reading if there's no more data coming.
                        if (result.IsCompleted)
                        {
                            if (buffer.Length > 0) Sys.ILog.Error($"OMS='{PortNO}', InPipe='{Encoding.ASCII.GetString(buffer.ToArray())}'");
                            break;
                        }
                    }
                    finally
                    {
                        reader.AdvanceTo(buffer.Start, buffer.End);
                    }
                }
            }
            catch (Exception ex)
            {
                Sys.ILog.Error($"OMS='{PortNO}', Error='{ex}'");
            }
            finally
            {
                // Mark the PipeReader as complete.
                await reader.CompleteAsync();
                Conn?.Close();
                BeatTimer.Dispose();

                Sys.ActSys.WhenTerminated.Wait(3000);
                Connecting();
            }
        }
        #endregion

        #region Slice message daata from buffer
        bool TryReadLine(ref ReadOnlySequence<byte> buffer, out ReadOnlySequence<byte> line)
        {
            line = default;                        
            if (buffer.Length < PackOms.BuffMinLength) return false;

            SequencePosition? stx = buffer.Slice(0, 1).PositionOf(PackOms.STX);
            if (stx == null) return false;

            // 전문의 송신 길이
            int len = int.Parse(Encoding.ASCII.GetString(buffer.Slice(1, 4).ToArray()));

            // 버퍼에 길이가 전체 전문 길이보다 작을 경우
            if (buffer.Length < len) return false;

            line    = buffer.Slice(0, len);
            buffer  = buffer.Slice(len);
            return true;
        }
        #endregion

        #region Message Data Parsing
        void Parsing(ReadOnlySequence<byte> pck)
        {
            RecvLastTime = DateTime.Now;

            try
            {
                var head = new MsgHeader();
                head.Deserialize(pck.Slice(0, head.PacketLength).ToArray());

                // Ack POLL
                if (head.OpCode == PackOms.POLL)
                {
                    head.OP_CODE = PackOms.POOK.ToCharArray();
                    head.SEQ_NUM = Sys.LastSeq.Seq2.ToString().PadLeft(10, PackOms.ZERO).ToCharArray();                    
                    Conn.Send(head.Serialize());
                    return;
                }
                
                // Success Access
                if (head.OpCode == PackOms.LIOK || head.OpCode == PackOms.DLOK)
                {
                    Sys.ILog.Information($"OMS='{PortNO}', OpCode='{head.OpCode}', RecvSeqNum='{head.SeqNum}', LastSe='{Sys.LastSeq.Seq2}'");

                    // Set Last Sequence : AXE will recovery data from last sequence
                    if (Sys.LastSeq.Seq2 < head.SeqNum) Sys.LastSeq.Seq2 = head.SeqNum;
                }
                // 16=JOB Done, 21=Requeest Order New, 22=Request Order Cancel, 90=JOB Emergency Cancel
                else if (head.OpCode == PackOms.DATA)
                {
                    var data = new DataHeader();
                    data.Deserialize(pck.Slice(0, data.PacketLength).ToArray());

                    if (data.HEAD.SeqNum != Sys.LastSeq.Seq2 + 1)
                    {
                        Sys.ILog.Warning($"Sequence Error. OMS='{PortNO}', RecvSeqNum='{data.HEAD.SeqNum}', LastSeq='{Sys.LastSeq.Seq2}'");
                        Conn.Close();
                        return;
                    }

                    if (data.HEAD.OpCode == PackOms.DATA)
                    {
                        Sys.LastSeq.Seq2 = data.HEAD.SeqNum;

                        // 90=JOB Emergency Cancel, 16=JOB Done
                        if (data.BODY.SvcType == PackOms.JobEmergency || data.BODY.SvcType == PackOms.JobDone)
                        {
                            var rcv = new ReqJobOrdCncl();
                            rcv.Deserialize(pck.ToArray());

                            var sb = new StringBuilder();
                            sb.Append($"SvcType='{rcv.BODY.SvcType}', ");
                            sb.Append($"TrCode='{rcv.TrCode}', ");
                            sb.Append($"ReqID='{rcv.ReqID}', ");
                            sb.Append($"IssueCode='{rcv.IssueCode}', ");
                            sb.Append($"NotifyMsg='{rcv.NotiMsg}'");
                            Sys.ILog.Information(sb.ToString());
                        }
                        // 21=Requeest Order New, 22=Request Order Cancel
                        else
                        {
                            var rcv = new ReqOrder();
                            rcv.Deserialize(pck.ToArray());

                            var sb = new StringBuilder();
                            sb.Append($"SvcType='{rcv.BODY.SvcType}', ");
                            sb.Append($"TrCode='{rcv.TrCode}', ");
                            sb.Append($"ReqID='{rcv.ReqID}', ");
                            sb.Append($"ActionID='{rcv.ActionId}', ");
                            sb.Append($"OrigOrderID='{rcv.OrigOrderID}', ");
                            sb.Append($"IssueCode='{rcv.IssueCode}', ");
                            sb.Append($"AskBid='{rcv.ASK_BID_TYPE}', ");
                            sb.Append($"OrdQty='{rcv.OrdQty}', ");
                            sb.Append($"Partial='{rcv.PARTIAL_FLAG}', ");
                            sb.Append($"OrdPrc='{rcv.OrdPrc}', ");
                            sb.Append($"OrdType='{rcv.ORDER_TYPE}', ");
                            sb.Append($"UserID='{rcv.UserID}'");
                            Sys.ILog.Information(sb.ToString());
                        }
                    }
                }
                // OP_CODE Error or not exepect something else
                else
                {
                    Sys.ILog.Information($"OMS='{PortNO}', OpCode='{head.OpCode}', RecvSeqNum='{head.SeqNum}', LastSeq='{Sys.LastSeq.Seq2}'");
                    Conn.Close();
                }
            }
            catch (Exception ex)
            {
                Sys.ILog.Error($"OMS='{PortNO}', Error='{ex}'");
            }
        }
        #endregion

        #region Request Authentication To MAX
        void SendAccessToMax()
        {
            var HEAD        = new MsgHeader();
            HEAD.STX        = HEAD.Stx;
            HEAD.LENGTH     = HEAD.PacketLength.ToString().PadLeft(4, PackOms.ZERO).ToCharArray();
            HEAD.ACCESS_ID  = Sys.AccessID.ToCharArray();
            HEAD.SEND_TIME  = HEAD.SendTime;
            HEAD.OP_CODE    = (Sys.LastSeq.Seq2 == 0 ? PackOms.LINK : PackOms.DLNK).ToCharArray();
            HEAD.SEQ_NUM    = Sys.LastSeq.Seq2.ToString().PadLeft(10, PackOms.ZERO).ToCharArray();
            HEAD.CNT        = HEAD.CntZero;
            HEAD.ASYNCTP    = HEAD.SyncTp;
            HEAD.FILLER     = HEAD.Filler;
            var data        = HEAD.Serialize();

            Conn.Send(data);
        }
        #endregion

        #region HeartBeat Check Timer : Received Last Message Time
        void SetHeartbeatTimer()
        {
            if (BeatTimer != null && BeatTimer.Enabled) return;

            BeatTimer           = new System.Timers.Timer(PackOms.HeartbeatTimeout * 1000);
            BeatTimer.Elapsed   += OnBeatTime;
            BeatTimer.AutoReset = true;
            BeatTimer.Enabled   = true;
        }

        void OnBeatTime(object source, ElapsedEventArgs e)
        {
            if (Conn.Connected && RecvLastTime.AddSeconds(PackOms.HeartbeatTimeout).CompareTo(DateTime.Now) < 0)
            {
                Sys.ILog.Warning($"Hearbeat Warning. OMS='{PortNO}', RecvLastTime='{RecvLastTime:HH:mm:ss.fffff}'");                
                Conn.Close();
            }
        }
        #endregion

        #region Actor Mail Receiver
        protected override void OnReceive(object obj)
        {

        }
        #endregion
    }
}