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
    // Actor Name   : /user/1
    // Description  : JOB Regis/Cancle Request/Ack
    internal class OmsTcp1 : UntypedActor
    {
        #region Instance
        const int PortNO    = 1;
        const int PollSec   = 5;
        
        Socket Conn { get; set; }
        DateTime SendMsgTime { get; set; }
        System.Timers.Timer PollTimer { get; set; }
        #endregion

        #region Main
        public OmsTcp1()
        {
            Connecting();
            SetPollTimer();
        }
        #endregion

        #region Connecting TCP(client)
        async void Connecting()
        {
            var ipAddr   = IPAddress.Parse(Sys.IConfDB["Max:Active:Ip"]);
            var port     = Sys.IConfDB[$"Max:Active:Port:1"];
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
            var pipe     = new Pipe();
            Task writing = FillPipeAsync(socket, pipe.Writer);
            Task reading = ReadPipeAsync(pipe.Reader);

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
                await writer.CompleteAsync();
            }
        }
        #endregion

        #region Read in pipeline
        async Task ReadPipeAsync(PipeReader reader)
        {
            SendAccessToMax();

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

            // message length
            int length = int.Parse(Encoding.ASCII.GetString(buffer.Slice(1, 4).ToArray()));

            // it is skip if message lenght is smaller buffer lenght
            if (buffer.Length < length) return false;

            line    = buffer.Slice(0, length);
            buffer  = buffer.Slice(length);
            return true;
        }
        #endregion

        #region Message Data Parsing
        void Parsing(ReadOnlySequence<byte> pck)
        {
            try
            {
                var head = new MsgHeader();                
                head.Deserialize(pck.Slice(0, head.PacketLength).ToArray());

                // It doesn't need to check heartbeat(POOK) ack message
                if (head.OpCode == PackOms.POOK) return;


                // Success Access
                if (head.OpCode == PackOms.LIOK || head.OpCode == PackOms.DLOK)
                {
                    Sys.ILog.Information($"OMS='{PortNO}', OpCode='{head.OpCode}', RecvSeqNum='{head.SeqNum}', LastSeq='{Sys.LastSeq.Seq1}'");

                    if (Sys.LastSeq.Seq1 != head.SeqNum) Sys.LastSeq.Seq1 = head.SeqNum;
                }
                // 12=AckJobRegis, 14=AckJobCancel
                else if (head.OpCode == PackOms.DAOK)
                {
                    var rcv = new RespAckJob();
                    rcv.Deserialize(pck.ToArray());

                    var sb = new StringBuilder();
                    sb.Append($"OMS='{PortNO}', ");
                    sb.Append($"RecvSeq='{head.SeqNum}', ");
                    sb.Append($"ReqID='{rcv.ReqId}', ");
                    sb.Append($"ErrFlag='{rcv.ERROR_FLAG}', ");
                    if (rcv.ERROR_FLAG == 'Y') sb.Append($"ErrMsg='{rcv.ErrMsg.Trim()}'");
                    Sys.ILog.Information(sb.ToString());
                }
                // OP_CODE Error or not exepect something else
                else
                {
                    Sys.ILog.Information($"OMS='{PortNO}', OpCode='{head.OpCode}', RecvSeqNum='{head.SeqNum}' LastSeq='{Sys.LastSeq.Seq1}'");
                    Conn?.Close();
                }
            }
            catch (Exception ex)
            {
                Sys.ILog.Error($"OMS='{PortNO}', Error='{ex}'");
            }
        }
        #endregion

        #region Actor Mail Receiver
        // Send Message To MAX : Request JOB Regis or Request JOB Cancel
        protected override void OnReceive(object obj)
        {
            var mail = (Mail)obj;

            try
            {
                var req                 = (ReqJob)mail.MsgData;
                req.HEAD.STX            = req.HEAD.Stx;
                req.HEAD.LENGTH         = req.PacketLength;
                req.HEAD.ACCESS_ID      = Sys.AccessID.ToCharArray();
                req.HEAD.SEND_TIME      = req.HEAD.SendTime;
                req.HEAD.ASYNCTP        = req.HEAD.AsyncTp;
                req.HEAD.FILLER         = req.HEAD.Filler;
                req.HEAD.OP_CODE        = PackOms.DATA.ToCharArray();
                req.HEAD.SEQ_NUM        = (Sys.LastSeq.Seq1 += 1).ToString().PadLeft(10, PackOms.ZERO).ToCharArray();
                req.HEAD.CNT            = req.HEAD.CntData;
        
                req.BODY.DATA_TYPE      = req.BODY.DataType;
                req.BODY.SERVICE_TYPE   = mail.SvcType.ToCharArray();
                req.BODY.RESPOND_CODE   = req.BODY.RespCode;
                req.BODY.FILLER         = req.BODY.Filler;
                var data                = req.Serialize();

                if (Conn != null && Conn.Connected)
                {                    
                    Conn.Send(data);
                    SendMsgTime = DateTime.Now;
                }
            }
            catch (Exception ex)
            {
                Sys.ILog.Error($"OMS='{PortNO}', Type='{mail.SvcType}', Error='{ex}'");
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
            HEAD.OP_CODE    = (Sys.LastSeq.Seq1 == 0 ? PackOms.LINK : PackOms.DLNK).ToCharArray();
            HEAD.SEQ_NUM    = Sys.LastSeq.Seq1.ToString().PadLeft(10, PackOms.ZERO).ToCharArray();
            HEAD.CNT        = HEAD.CntZero;
            HEAD.ASYNCTP    = HEAD.SyncTp;
            HEAD.FILLER     = HEAD.Filler;
            var data        = HEAD.Serialize();

            Conn.Send(data);
        }
        #endregion

        #region HeartBeat Timer : POOL Send to MAX
        void SetPollTimer()
        {
            if (PollTimer != null) return;

            PollTimer           = new System.Timers.Timer(PollSec * 1000);            
            PollTimer.Elapsed   += OnPollTime;
            PollTimer.AutoReset = true;
            PollTimer.Enabled   = true;
        }

        void OnPollTime(object source, ElapsedEventArgs e)
        {
            if (Conn.Connected && DateTime.Now > SendMsgTime.AddSeconds(PollSec))
            {
                var HEAD        = new MsgHeader();
                HEAD.STX        = HEAD.Stx;
                HEAD.LENGTH     = HEAD.PacketLength.ToString().PadLeft(4, PackOms.ZERO).ToCharArray();
                HEAD.ACCESS_ID  = Sys.AccessID.ToCharArray();
                HEAD.SEND_TIME  = HEAD.SendTime;
                HEAD.OP_CODE    = PackOms.POLL.ToCharArray();
                HEAD.SEQ_NUM    = Sys.LastSeq.Seq1.ToString().PadLeft(10, PackOms.ZERO).ToCharArray();
                HEAD.CNT        = HEAD.CntZero;
                HEAD.ASYNCTP    = HEAD.AsyncTp;
                HEAD.FILLER     = HEAD.Filler;
                var pck         = HEAD.Serialize();

                Conn.Send(pck);
                SendMsgTime = DateTime.Now;
            }
        }
        #endregion
    }
}
