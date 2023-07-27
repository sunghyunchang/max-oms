using System.Runtime.InteropServices;

namespace maxoms.models
{
    internal class PackOms
    {
        #region const
        public const byte STX               = 0x02;
        public const char SPACE             = ' ';
        public const char ZERO              = '0';
        public const int BuffMinLength      = 6;
        public const int HeartbeatTimeout   = 30;
        public const string TimeFormat      = "HHmmssfff";
        #endregion

        #region Const OP_CODE
        public const string LINK            = "LINK"; // Initial Authentication When Sequence=0
        public const string LIOK            = "LIOK";
        public const string DLNK            = "DLNK"; // Retry Autentication after Data (Sequence > 0)
        public const string DLOK            = "DLOK";
        public const string DATA            = "DATA";
        public const string DAOK            = "DAOK";
        public const string POLL            = "POLL";
        public const string POOK            = "POOK";

        public const string E001            = "E001"; // STX Error
        public const string E002            = "E002"; // Message Length Error
        public const string E003            = "E003"; // AccessID (unique) does not exist.
        public const string E006            = "E006"; // Sequence Error
        #endregion

        #region SERVICE_TYPE (Mail)
        public const string ReqJobRegis     = "11"; // Request JOB Regis
        public const string AckJobRegis     = "12"; // Ack JOB Regis
        public const string ReqJobCancel    = "13"; // Request JOB Cancel 
        public const string AckJobCancel    = "14"; // Ack JOB Cancel
        public const string JobDone         = "16"; // JOB Done
        public const string ReqOrderNew     = "21"; // Request Order New
        public const string ReqOrderCancel  = "22"; // Request Order Cancel
        public const string ReqOrderReplace = "23"; // Request Order Replace
        public const string AckOrderNew     = "31"; // Ack Order New
        public const string AckOrderCancel  = "32"; // Ack Order Cancel
        public const string AckOrderReplace = "33"; // Ack Order Replace
        public const string AckOrderExec    = "41"; // Order Exec
        public const string JobEmergency    = "90"; // JOB Emergency
        #endregion

        #region TR_CODE
        public const string TR_JOB_REGIS        = "TCHAOR10001";
        public const string TR_JOB_CANCEL       = "TCHAOR10003";
        public const string TR_JOB_DONE         = "TTRODP11307";
        public const string TR_JOB_EMERGENCY    = "TTRODP11303";

        public const string TR_ORDER_NEW        = "TCHODR10001";
        public const string TR_ORDER_CANCEL     = "TCHODR10003";
        public const string TR_ORDER_REPLACE    = "TCHODR10005";
        public const string TR_ORDER_NORMAL     = "TTRODP11301";
        public const string TR_ORDER_REJECT     = "TTRODP11321";
        public const string TR_ORDER_EXEC       = "TTRTDP21301";
        #endregion

        public static int MsgHeaderLength { get { return Marshal.SizeOf(typeof(MsgHeader)); } }
    }

    
    #region Header
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 1)]
    public struct MsgHeader
    {
        public byte STX;
        public byte Stx { get { return 0x02; } }

        // Length
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
        public char[] LENGTH;
        public int Length { get { return int.Parse(new string(LENGTH)); } }

        // Access ID
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)]
        public char[] ACCESS_ID;
        public string AccessId { get { return new string(ACCESS_ID); } }

        // SendTime (HHmmssfff)
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 9)]
        public char[] SEND_TIME;
        public char[] SendTime { get { return DateTime.Now.ToString(PackOms.TimeFormat).ToCharArray(); } }

        // Operation Code
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
        public char[] OP_CODE;
        public string OpCode { get { return new string(OP_CODE); } }

        // Sequence Number
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 10)]
        public char[] SEQ_NUM;
        public uint SeqNum { get { return uint.Parse(new string(SEQ_NUM)); } }

        // Message Count
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public char[] CNT;
        public char[] CntZero { get { return "00".ToCharArray(); } }
        public char[] CntData { get { return "01".ToCharArray(); } }
       
        // Communiccate Mode 0=SYNC 1=ASYNC
        public char ASYNCTP;
        public char SyncTp { get { return '0'; } }
        public char AsyncTp { get { return '1'; } }

        // Space
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
        public char[] FILLER;
        public char[] Filler { get { return "".PadRight(3, PackOms.SPACE).ToCharArray(); } }

        public byte[] Serialize()
        {
            // allocate a byte array for the struct data
            var buffer = new byte[Marshal.SizeOf(typeof(MsgHeader))];

            // Allocate a GCHandle and get the array pointer
            var gch     = GCHandle.Alloc(buffer, GCHandleType.Pinned);
            var pBuffer = gch.AddrOfPinnedObject();

            // copy data from struct to array and unpin the gc pointer
            Marshal.StructureToPtr(this, pBuffer, false);
            gch.Free();

            return buffer;
        }

        public void Deserialize(byte[] data)
        {
            var gch = GCHandle.Alloc(data, GCHandleType.Pinned);
            this    = (MsgHeader)Marshal.PtrToStructure(gch.AddrOfPinnedObject(), typeof(MsgHeader));
            gch.Free();
        }

        public int PacketLength { get { return Marshal.SizeOf(typeof(MsgHeader)); } }
    }
    #endregion

    #region Body Data Header
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 1)]
    public struct BodyHeader
    {
        // L=Algo
        public char DATA_TYPE;
        public char DataType { get { return 'L'; } }

        // Service Type(11, 12, 13, 14, 21, 22, 31, 32, 41, 90)
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public char[] SERVICE_TYPE;
        public string SvcType { get { return new string(SERVICE_TYPE); } }

        // Normal=0000
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
        public char[] RESPOND_CODE;
        public char[] RespCode { get { return "0000".ToCharArray(); } }

        // Space
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 43)]
        public char[] FILLER;
        public char[] Filler { get { return "".PadRight(43, PackOms.SPACE).ToCharArray(); } }

        public byte[] Serialize()
        {
            // allocate a byte array for the struct data
            var buffer = new byte[Marshal.SizeOf(typeof(BodyHeader))];

            // Allocate a GCHandle and get the array pointer
            var gch     = GCHandle.Alloc(buffer, GCHandleType.Pinned);
            var pBuffer = gch.AddrOfPinnedObject();

            // copy data from struct to array and unpin the gc pointer
            Marshal.StructureToPtr(this, pBuffer, false);
            gch.Free();

            return buffer;
        }

        public void Deserialize(byte[] data)
        {
            var gch = GCHandle.Alloc(data, GCHandleType.Pinned);
            this    = (BodyHeader)Marshal.PtrToStructure(gch.AddrOfPinnedObject(), typeof(BodyHeader));
            gch.Free();
        }

        public int PacketLength { get { return Marshal.SizeOf(typeof(BodyHeader)); } }
    }
    #endregion

    #region Header + Body Data Header

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 1)]
    struct DataHeader
    {
        public MsgHeader HEAD;

        public BodyHeader BODY;

        public byte[] Serialize()
        {
            var buffer  = new byte[Marshal.SizeOf(typeof(DataHeader))];
            var gch     = GCHandle.Alloc(buffer, GCHandleType.Pinned);
            var pBuffer = gch.AddrOfPinnedObject();
            Marshal.StructureToPtr(this, pBuffer, false);
            gch.Free();
            return buffer;
        }

        public void Deserialize(byte[] data)
        {
            var gch = GCHandle.Alloc(data, GCHandleType.Pinned);
            this    = (DataHeader)Marshal.PtrToStructure(gch.AddrOfPinnedObject(), typeof(DataHeader));
            gch.Free();
        }

        public int PacketLength { get { return Marshal.SizeOf(typeof(DataHeader)); } }
    }
    #endregion

    #region [OMS -> MAX] AI JOB Registration/Cancel Request
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 1)]
    public struct ReqJob
    {
        public MsgHeader HEAD;

        // Service Type=11, 13
        public BodyHeader BODY;

        // [CHAR] TCHAOR10001 = JOB Registration, TCHAOR10003 = JOB Cancel
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 11)]
        public char[] TR_CODE;
        public string TrCode { get { return new string(TR_CODE); } }

        // [NUM] Job Registration ID of OMS
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 10)]
        public char[] REQ_ID;
        public uint ReqID { get { return uint.Parse(new string(REQ_ID)); } }

        // [CHAR] Issue Code
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 12)]
        public char[] ISSUE_CODE;
        public string IssueCode { get { return new string(ISSUE_CODE).Trim(); } }

        // 1=ASK, 2=BID
        public char ASK_BID_TYPE;

        // [NUM] Order Quantity
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 10)]
        public char[] ORDER_QTY;
        public long OrderQty { get { return long.Parse(new string(ORDER_QTY)); } }

        // [NUM] Order Price
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 11)]
        public char[] ORDER_PRICE;
        public long OrderPrice { get { return long.Parse(new string(ORDER_PRICE)); } }

        // 0=AI(AXE)
        public char ORDER_TYPE;
        public char OrderType { get { return '0'; } }

        // [CHAR] User ID
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 12)]
        public char[] USER_ID;
        public string UserId { get { return new string(USER_ID).Trim(); } }

        // [CHAR] User IP Address
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 12)]
        public char[] USER_IP_ADDR;
        public string UserIp { get { return new string(USER_IP_ADDR).Trim(); } }

        // [CHAR] User Mac Address
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 12)]
        public char[] USER_MAC_ADDR;
        public string UserMac { get { return new string(USER_MAC_ADDR).Trim(); } }

        // [CHAR] 01=VWAP, 02=TWAP
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 5)]
        public char[] ALGO_TYPE;
        public string AlgoType { get { return new string(ALGO_TYPE).Trim(); } }

        // JOB Start Time (HHMMSS)
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)]
        public char[] START_TIME;
        public string StartTime { get { return new string(START_TIME); } }

        // JOB End Time (HHMMSS)
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)]
        public char[] END_TIME;
        public string EndTime { get { return new string(END_TIME); } }

        // Tolerance (1 ~ 99%), Default 20%
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
        public char[] RANGE_BOUND;
        public string RangeBound { get { return new string(RANGE_BOUND).Trim(); } }

        // Fee
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
        public char[] FEE;
        public string Fee { get { return new string(FEE).Trim(); } }

        // Space
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 46)]
        public char[] FILLER;
        public char[] Filler { get { return "".PadRight(46, PackOms.SPACE).ToCharArray(); } }

        public byte[] Serialize()
        {
            // allocate a byte array for the struct data
            var buffer = new byte[Marshal.SizeOf(typeof(ReqJob))];

            // Allocate a GCHandle and get the array pointer
            var gch     = GCHandle.Alloc(buffer, GCHandleType.Pinned);
            var pBuffer = gch.AddrOfPinnedObject();

            // copy data from struct to array and unpin the gc pointer
            Marshal.StructureToPtr(this, pBuffer, false);
            gch.Free();

            return buffer;
        }

        public void Deserialize(byte[] data)
        {
            var gch = GCHandle.Alloc(data, GCHandleType.Pinned);
            this    = (ReqJob)Marshal.PtrToStructure(gch.AddrOfPinnedObject(), typeof(ReqJob));
            gch.Free();
        }

        public char[] PacketLength { get { return Marshal.SizeOf(typeof(ReqJob)).ToString().PadLeft(4, '0').ToCharArray(); } }
    }
    #endregion

    #region [MAX -> OMS] AI JOB Registration/Cancel Acknowledgement
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 1)]
    struct RespAckJob
    {
        public MsgHeader HEAD;

        // Service Type=12, 14
        public BodyHeader BODY;

        // [NUM] Job Registration ID of OMS
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 10)]
        public char[] REQ_ID;
        public uint ReqId { get { return uint.Parse(new string(REQ_ID)); } }

        // Y=Error, N=No Error
        public char ERROR_FLAG;

        // Error Reason
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 80)]
        public char[] ERROR_MSG;
        public string ErrMsg { get { return new string(ERROR_MSG); } }

        // Space
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 50)]
        public char[] FILLER;
        public char[] Filler { get { return "".PadRight(50, PackOms.SPACE).ToCharArray(); } }

        public byte[] Serialize()
        {
            // allocate a byte array for the struct data
            var buffer = new byte[Marshal.SizeOf(typeof(RespAckJob))];

            // Allocate a GCHandle and get the array pointer
            var gch     = GCHandle.Alloc(buffer, GCHandleType.Pinned);
            var pBuffer = gch.AddrOfPinnedObject();

            // copy data from struct to array and unpin the gc pointer
            Marshal.StructureToPtr(this, pBuffer, false);
            gch.Free();

            return buffer;
        }

        public void Deserialize(byte[] data)
        {
            var gch = GCHandle.Alloc(data, GCHandleType.Pinned);
            this    = (RespAckJob)Marshal.PtrToStructure(gch.AddrOfPinnedObject(), typeof(RespAckJob));
            gch.Free();
        }
    }
    #endregion

    #region [MAX -> OMS] New/Cancel Order Request
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 1)]
    public struct ReqOrder
    {
        public MsgHeader HEAD;

        // Service Type=21, 22
        public BodyHeader BODY;

        // TCHODR10001=New Order, TCHODR10003=Cancel Order
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 11)]
        public char[] TR_CODE;
        public string TrCode { get { return new string(TR_CODE); } }

        #region ORDER_ID[20] = REQ_ID[10] + ACTION_ID[10]
        // [NUM] Job Registration ID of OMS
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 10)]
        public char[] REQ_ID;
        public uint ReqID { get { return uint.Parse(new string(REQ_ID)); } }

        // Actiond ID of AXE
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 10)]
        public char[] ACTION_ID;
        public string ActionId { get { return new string(ACTION_ID).Trim(); } }
        #endregion

        // Original Order ID of OMS for Cancel Order
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 10)]
        public char[] ORIG_ORDER_ID;
        public uint OrigOrderID { get { return uint.Parse(new string(ORIG_ORDER_ID)); } }

        // Issue Code
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 12)]
        public char[] ISSUE_CODE;
        public string IssueCode { get { return new string(ISSUE_CODE).Trim(); } }

        // 1=ASK, 2=BID
        public char ASK_BID_TYPE;

        // Order Quantity
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 10)]
        public char[] ORDER_QTY;
        public long OrdQty { get { return long.Parse(new string(ORDER_QTY)); } }

        // 1=Full Cancel, 2=Partial Cancel
        public char PARTIAL_FLAG;

        // Order Price
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 11)]
        public char[] ORDER_PRICE;
        public decimal OrdPrc { get { return long.Parse(new string(ORDER_PRICE)); } }

        // 1=Market, 2=Limit
        public char ORDER_TYPE;

        // User ID
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 12)]
        public char[] USER_ID;
        public string UserID { get { return new string(USER_ID).Trim(); } }

        // User IP Address
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 12)]
        public char[] USER_IP_ADDR;

        // User MAC Address
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 12)]
        public char[] USER_MAC_ADDR;

        // Space
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 50)]
        public char[] FILLER;
        public char[] Filler { get { return "".PadRight(50, PackOms.SPACE).ToCharArray(); } }

        public byte[] Serialize()
        {
            // allocate a byte array for the struct data
            var buffer = new byte[Marshal.SizeOf(typeof(ReqOrder))];

            // Allocate a GCHandle and get the array pointer
            var gch     = GCHandle.Alloc(buffer, GCHandleType.Pinned);
            var pBuffer = gch.AddrOfPinnedObject();

            // copy data from struct to array and unpin the gc pointer
            Marshal.StructureToPtr(this, pBuffer, false);
            gch.Free();

            return buffer;
        }

        public void Deserialize(byte[] data)
        {
            var gch = GCHandle.Alloc(data, GCHandleType.Pinned);
            this    = (ReqOrder)Marshal.PtrToStructure(gch.AddrOfPinnedObject(), typeof(ReqOrder));
            gch.Free();
        }
    }
    #endregion

    #region [OMS -> MAX] New/Cancel Order Acknowledgement & Execution
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 1)]
    public struct RespOrder
    {
        public MsgHeader HEAD;

        // Service Type=31, 32, 41
        public BodyHeader BODY;

        // TTRODP11301=Normal(New/Cancel Acknowledgement)
        // TTRODP11321=Refuse(Refused by one fo the Exechange or Ledger or OMS.)
        // TTRTDP21301=Execution
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 11)]
        public char[] TR_CODE;
        public string TrCode { get { return new string(TR_CODE); } }

        #region ORDER_ID[20] = REQ_ID[10] + ACTION_ID[10]
        
        // [NUM] Job Registration ID of OMS
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 10)]
        public char[] REQ_ID;
        public uint ReqID { get { return uint.Parse(new string(REQ_ID)); } }

        // Actiond ID of AXE
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 10)]
        public char[] ACTION_ID;
        public string ActionId { get { return new string(ACTION_ID).Trim(); } }
        #endregion

        // [NUM] Order ID of OMS
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 10)]
        public char[] ORDER_ID;
        public uint OrderID { get { return uint.Parse(new string(ORDER_ID)); } }

        // [NUM] Original Order ID of OMS When Cancel Order
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 10)]
        public char[] ORIG_ORDER_ID;
        public uint OrigOrderID { get { return uint.Parse(new string(ORIG_ORDER_ID)); } }

        // Issue Code
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 12)]
        public char[] ISSUE_CODE;

        // 1=ASK, 2=BID
        public char ASK_BID_TYPE;

        // 1=NEW, 2=REPLACE, 3=CANCEL
        public char PLC_TYPE;

        // [NUM] Order Quantity
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 10)]
        public char[] ORDER_QTY;
        public long OrdQty { get { return long.Parse(new string(ORDER_QTY)); } }

        // [NUM] Order Price
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 11)]
        public char[] ORDER_PRICE;

        // 1=Market, 2=Limit
        public char ORDER_TYPE;

        // Confirmed Quantity for Cancel Order
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 10)]
        public char[] CONFIRM_QTY;
        public long CfrmQty { get { return long.Parse(new string(CONFIRM_QTY)); } }

        // [NUM] Trading Number
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 11)]
        public char[] TRADING_NO;
        public long TradeNo { get { return long.Parse(new string(TRADING_NO)); } }

        // [NUM] Trading Price
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 11)]
        public char[] TRADING_PRICE;
        public decimal TradePrc { get { return long.Parse(new string(TRADING_PRICE)); } }

        // [NUM] Trading Volume
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 10)]
        public char[] TRADING_VOLUME;
        public long TradeQty { get { return long.Parse(new string(TRADING_VOLUME)); } }

        // Trading Time (HHMMSSsss)
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 9)]
        public char[] TRADING_TIME;
        public char[] TradingTime { get { return DateTime.Now.ToString(PackOms.TimeFormat).ToCharArray(); } }

        // Error Code
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 10)]
        public char[] ERROR_CODE;
        public string ErrCode { get { return new string(ERROR_CODE); } }

        // Error Reason (Free Format)
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 80)]
        public char[] ERROR_MSG;
        public string ErrMsg { get { return new string(ERROR_MSG); } }

        // Space
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 40)]
        public char[] FILLER;
        public char[] Filler { get { return "".PadRight(40, PackOms.SPACE).ToCharArray(); } }

        public byte[] Serialize()
        {
            // allocate a byte array for the struct data
            var buffer = new byte[Marshal.SizeOf(typeof(RespOrder))];

            // Allocate a GCHandle and get the array pointer
            var gch     = GCHandle.Alloc(buffer, GCHandleType.Pinned);
            var pBuffer = gch.AddrOfPinnedObject();

            // copy data from struct to array and unpin the gc pointer
            Marshal.StructureToPtr(this, pBuffer, false);
            gch.Free();

            return buffer;
        }

        public void Deserialize(byte[] data)
        {
            var gch = GCHandle.Alloc(data, GCHandleType.Pinned);
            this    = (RespOrder)Marshal.PtrToStructure(gch.AddrOfPinnedObject(), typeof(RespOrder));
            gch.Free();
        }
        
        public char[] PacketLength { get { return Marshal.SizeOf(typeof(RespOrder)).ToString().PadLeft(4, '0').ToCharArray(); } }
    }
    #endregion

    #region [MAX -> OMS] AI JOB Emergency Cancel Request
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 1)]
    public struct ReqJobOrdCncl
    {
        public MsgHeader HEAD;

        // Service Type=90, 16
        public BodyHeader BODY;

        // [CHAR] TTRODP11303=AI JOB Emergency Cancel, TTRODP11307=JOB Done
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 11)]
        public char[] TR_CODE;
        public string TrCode { get { return new string(TR_CODE); } }

        // [NUM] Job Registration ID of OMS
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 10)]
        public char[] REQ_ID;
        public uint ReqID { get { return uint.Parse(new string(REQ_ID)); } }

        // [CHAR] ISSUE_CODE
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 12)]
        public char[] ISSUE_CODE;
        public string IssueCode { get { return new string(ISSUE_CODE).Trim(); } }

        // Notify Reason
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 80)]
        public char[] NOTIFY_REASON;
        public string NotiMsg { get { return new string(NOTIFY_REASON).Trim(); } }

        // Space
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 50)]
        public char[] FILLER;
        public char[] Filler { get { return "".PadRight(50, PackOms.SPACE).ToCharArray(); } }

        public byte[] Serialize()
        {
            // allocate a byte array for the struct data
            var buffer = new byte[Marshal.SizeOf(typeof(ReqJobOrdCncl))];

            // Allocate a GCHandle and get the array pointer
            var gch     = GCHandle.Alloc(buffer, GCHandleType.Pinned);
            var pBuffer = gch.AddrOfPinnedObject();

            // copy data from struct to array and unpin the gc pointer
            Marshal.StructureToPtr(this, pBuffer, false);
            gch.Free();

            return buffer;
        }

        public void Deserialize(byte[] data)
        {
            var gch = GCHandle.Alloc(data, GCHandleType.Pinned);
            this    = (ReqJobOrdCncl)Marshal.PtrToStructure(gch.AddrOfPinnedObject(), typeof(ReqJobOrdCncl));
            gch.Free();
        }
    }
    #endregion
}