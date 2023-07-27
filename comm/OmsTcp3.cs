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
    // Actor Name   : /user/3
    // Description  : New/Cancel Order Ack & Execution
    internal class OmsTcp3 : UntypedActor
    {
        #region Instance
        const int PortNO    = 3;
        const int PollSec   = 5;

        Socket Conn { get; set; }
        DateTime SendMsgTime { get; set; }
        System.Timers.Timer PollTimer { get; set; }
        #endregion

        #region Main
        public OmsTcp3()
        {
            Connecting();
            SetPollTimer();
        }
        #endregion

        #region Connecting TCP(client)
        async void Connecting()
        {
            var ipAddr   = IPAddress.Parse(Sys.IConfDB["Max:Active:Ip"]);
            var port     = Sys.IConfDB[$"Max:Active:Port:3"];
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
                    Sys.ILog.Information($"OMS='{PortNO}', OpCode='{head.OpCode}', RecvSeqNum='{head.SeqNum}', LastSeq='{Sys.LastSeq.Seq3}'");

                    if (head.SeqNum < Sys.LastSeq.Seq3)
                    {
                        Sys.LastSeq.Seq3 = head.SeqNum;

                        // * Responsibility Recovery Data from what received last sequece of AXE
                    }
                }
                // OP_CODE Error or not exepect something else
                else
                {
                    Sys.ILog.Information($"OMS='{PortNO}', OpCode='{head.OpCode}', RecvSeqNum='{head.SeqNum}', LastSeq='{Sys.LastSeq.Seq3}'");
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
            MsgHeader HEAD  = new MsgHeader();
            HEAD.STX        = HEAD.Stx;
            HEAD.LENGTH     = HEAD.PacketLength.ToString().PadLeft(4, PackOms.ZERO).ToCharArray();
            HEAD.ACCESS_ID  = Sys.AccessID.ToCharArray();
            HEAD.SEND_TIME  = HEAD.SendTime;
            HEAD.OP_CODE    = (Sys.LastSeq.Seq3 == 0 ? PackOms.LINK : PackOms.DLNK).ToCharArray();
            HEAD.SEQ_NUM    = Sys.LastSeq.Seq3.ToString().PadLeft(10, PackOms.ZERO).ToCharArray();
            HEAD.CNT        = HEAD.CntZero;
            HEAD.ASYNCTP    = HEAD.SyncTp;
            HEAD.FILLER     = HEAD.Filler;
            var data        = HEAD.Serialize();

            Conn.Send(data);
        }
        #endregion

        #region Actor Mail Receiver
        // Send Message To MAX : Ack Order New/Cancel or Execution
        protected override void OnReceive(object obj)
        {
            var mail = (Mail)obj;

            try
            {   
                var ack                 = (RespOrder)mail.MsgData;
                ack.HEAD.STX            = ack.HEAD.Stx;
                ack.HEAD.LENGTH         = ack.PacketLength;
                ack.HEAD.ACCESS_ID      = Sys.AccessID.ToCharArray();
                ack.HEAD.SEND_TIME      = ack.HEAD.SendTime;
                ack.HEAD.OP_CODE        = PackOms.DATA.ToCharArray();
                ack.HEAD.SEQ_NUM        = (Sys.LastSeq.Seq3 += 1).ToString().PadLeft(10, PackOms.ZERO).ToCharArray();
                ack.HEAD.CNT            = ack.HEAD.CntData;
                ack.HEAD.ASYNCTP        = ack.HEAD.AsyncTp; 
                ack.HEAD.FILLER         = ack.HEAD.Filler;

                ack.BODY.DATA_TYPE      = ack.BODY.DataType;
                ack.BODY.SERVICE_TYPE   = mail.SvcType.ToCharArray();
                ack.BODY.RESPOND_CODE   = ack.BODY.RespCode;
                ack.BODY.FILLER         = ack.BODY.Filler;
                var pck                 = ack.Serialize();

                if (Conn != null && Conn.Connected)
                {
                    Conn.Send(pck);
                    SendMsgTime = DateTime.Now;
                }
            }
            catch (Exception ex)
            {
                Sys.ILog.Error($"OMS='{PortNO}', Type='{mail.SvcType}', Error='{ex}'");
            }
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
                HEAD.SEQ_NUM    = Sys.LastSeq.Seq3.ToString().PadLeft(10, PackOms.ZERO).ToCharArray();
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