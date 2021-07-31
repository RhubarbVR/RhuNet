using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using MessagePack;
namespace RhuNetShared
{
    public enum ConnectionTypes:byte { Unknown, LAN, WAN }

    [MessagePackObject]
    public class ClientInfo : IP2PBase
    {
        [Key(1)]
        public string Name { get; set; }
        [Key(0)]
        public long ID { get; set; }

        [Key(2)]
        public string _ExternalEndpoint { get; set; }
        [Key(3)]
        public string _InternalEndpoint { get; set; }


        [IgnoreMember]
        public IPEndPoint ExternalEndpoint { get { try { return IPEndPoint.Parse(_ExternalEndpoint); } catch { return default(IPEndPoint); } } set { _ExternalEndpoint = value?.ToString(); } }
        [IgnoreMember]
        public IPEndPoint InternalEndpoint { get { try { return IPEndPoint.Parse(_InternalEndpoint); } catch { return default(IPEndPoint); } } set { _InternalEndpoint = value?.ToString(); } }
   
        [Key(4)]
        public int _ConnectionType { get; set; }
        [IgnoreMember]
        public ConnectionTypes ConnectionType { get { return (ConnectionTypes)_ConnectionType; } set { _ConnectionType = (int)value; } }
        [Key(5)]
        public bool UPnPEnabled { get; set; }
        [Key(6)]
        public List<string> _InternalAddresses
        {
            get
            {
                List<string> strings = new List<string>();
                foreach (var item in InternalAddresses)
                {
                    strings.Add(item.ToString());
                }
                return strings;
            }
            set
            {
                InternalAddresses.Clear();
                foreach (var item in value)
                {
                    InternalAddresses.Add(IPAddress.Parse(item));
                }
            } }

        [IgnoreMember]
        public List<IPAddress> InternalAddresses = new List<IPAddress>();        

        [IgnoreMember] //server use only
        public TcpClient Client;

        [IgnoreMember] //server use only
        public bool Initialized;
        public bool Update(ClientInfo CI)
        {
            if (ID == CI.ID)
            {
                foreach (PropertyInfo P in CI.GetType().GetProperties())
                    if (P.GetValue(CI) != null)
                        P.SetValue(this, P.GetValue(CI));

                if (CI.InternalAddresses.Count > 0)
                {
                    InternalAddresses.Clear();
                    InternalAddresses.AddRange(CI.InternalAddresses);
                }
            }

            return (ID == CI.ID);
        }
        public override string ToString()
        {
            if (ExternalEndpoint != null)
                return Name + " (" + ExternalEndpoint.Address + ")";
            else
                return Name + " (UDP Endpoint Unknown)";
        }
        public ClientInfo Simplified() => new ClientInfo()
        {
            Name = this.Name,
            ID = this.ID,
            InternalEndpoint = this.InternalEndpoint,
            ExternalEndpoint = this.ExternalEndpoint
        };
    }    
}
