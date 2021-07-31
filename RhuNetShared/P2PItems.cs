using System;
using MessagePack;
namespace RhuNetShared
{
    public enum NotificationsTypes { ServerShutdown, Disconnected }

    [MessagePackObject]
    public class getClient : IP2PBase
    {
        [Key(0)]
        public long ID { get; set; }

        [Key(1)]
        public long ClientID { get; set; }

        public getClient(long _Tag)
        {
            ClientID = _Tag;
        }
        public getClient()
        {

        }
    }


    [MessagePackObject]
    public class Notification : IP2PBase
    {
        [Key(0)]
        public long ID { get; set; }
        [Key(1)]
        public NotificationsTypes Type { get; set; }
        [Key(2)]
        public byte[] Tag { get; set; }

        public Notification(NotificationsTypes _Type, object _Tag)
        {
            Type = _Type;
            Tag = MessagePackSerializer.Serialize(_Tag);
        }
        public Notification()
        {

        }
    }

    [MessagePackObject]
    public class Message : IP2PBase
    {
        [Key(3)]
        public string From { get; set; }
        [Key(1)]
        public string To { get; set; }
        [Key(2)]
        public string Content { get; set; }
        [Key(0)]
        public long ID { get; set; }
        [Key(4)]
        public long RecipientID { get; set; }

        public Message(string from, string to, string content)
        {
            From = from;
            To = to;
            Content = content;
        }

        public Message()
        {

        }
    }

    [MessagePackObject]
    public class Req : IP2PBase
    {
        [Key(0)]
        public long ID { get; set; }
        [Key(1)]
        public long RecipientID { get; set; }

        public Req(long Sender_ID, long Recipient_ID)
        {
            ID = Sender_ID;
            RecipientID = Recipient_ID;
        }
        public Req()
        {

        }
    }

    [MessagePackObject]
    public class Ack : IP2PBase
    {
        [Key(0)]
        public long ID { get; set; }
        [Key(1)]
        public long RecipientID { get; set; }
        [Key(2)]
        public bool Responce { get; set; }

        public Ack(long Sender_ID)
        {
            ID = Sender_ID;
        }
        public Ack()
        {

        }
    }

    [MessagePackObject]
    public class KeepAlive : IP2PBase
    {
        [Key(0)]
        public long ID { get; set; }

        public KeepAlive()
        {

        }

    }


    [MessagePackObject]
    public class Data : IP2PBase
    {
        [Key(0)]
        public long ID { get; set; }

        [Key(1)]
        public byte[] data{ get; set; }

        public Data(byte[] _data)
        {
            data = _data;
        }
        public Data()
        {

        }

    }
}
