using System.Runtime.Serialization.Formatters.Binary;
using System.IO;
using MessagePack;
using System.Collections.Generic;
using System;

namespace RhuNetShared
{
    public static class ByteConverter
    {
        public static System.Type[] types = new System.Type[] { null,typeof(ClientInfo),typeof(Notification),typeof(Message),typeof(Req),typeof(Ack),typeof(KeepAlive),typeof(getClient),typeof(Data) };

        public static byte[] ToByteArray<T>(this T clientInfo) where T: IP2PBase
        {
            List<byte> data = new List<byte>(MessagePackSerializer.Serialize(clientInfo.GetType(),clientInfo));
            int typeint = (byte)Array.IndexOf(types, clientInfo.GetType());
            if (typeint == -1 || typeint >= types.Length)
            {
                throw new Exception("Error not Assinded Type " + clientInfo.GetType().FullName);
            }
            byte type = (byte)typeint;
            data.Insert(0, type);
            return data.ToArray();
        }

        public static IP2PBase ToP2PBase(this byte[] bytes)
        {
            try
            {
                List<byte> data = new List<byte>(bytes);
                Type e = types[data[0]];
                data.RemoveAt(0);
                IP2PBase clientInfo = (IP2PBase)MessagePack.MessagePackSerializer.Deserialize(e,data.ToArray());
                return clientInfo;
            }
            catch(Exception e)
            {
                throw new Exception("Failed To Deserilize" + e.ToString());
            }

        }
    }
}
