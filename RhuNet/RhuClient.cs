using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using RhuNetShared;
using System.Threading;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

namespace RhuNet
{
    public class RhuClient
    {
        private IPAddress _internetAccessAdapter;

        private TcpClient _tCPClient = new TcpClient();
        private readonly UdpClient _uDPClient = new UdpClient();

        public string Token
        {
            get
            {
                return LocalClientInfo.ID.ToString();
            }
        }

        public ClientInfo LocalClientInfo = new ClientInfo();
        private readonly List<ClientInfo> _clients = new List<ClientInfo>();
        private readonly List<Ack> _ackResponces = new List<Ack>();

        private Thread _threadTCPListen;
        private Thread _threadUDPListen;

        public event EventHandler<string> OnResultsUpdate;
        public event EventHandler<ClientInfo> OnClientAdded;
        public event EventHandler<ClientInfo> OnClientUpdated;
        public event EventHandler<ClientInfo> OnClientRemoved;
        public event EventHandler OnServerConnect;
        public event EventHandler<IPEndPoint> OnClientConnection;
        public event EventHandler<MessageReceivedEventArgs> OnMessageReceived;
        public event Action<Data, IPEndPoint> DataRecived; 

        private bool _tCPListen = false;
        public bool TCPListen
        {
            get { return _tCPListen; }
            set
            {
                _tCPListen = value;
                if (value)
                {
                    ListenTCP();
                }
            }
        }

        private bool _uDPListen = false;
        public bool UDPListen
        {
            get { return _uDPListen; }
            set
            {
                _uDPListen = value;
                if (value)
                {
                    ListenUDP();
                }
            }
        }
        public IPEndPoint ServerEndpoint;
        public RhuClient(string ip,int port ,string UUID)
        {
            ServerEndpoint = new IPEndPoint(IPAddress.Parse(ip), port);
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                _uDPClient.AllowNatTraversal(true);
                _uDPClient.Client.SetIPProtectionLevel(IPProtectionLevel.Unrestricted);
            }
            _uDPClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);

            LocalClientInfo.Name = UUID + "_"+ Guid.NewGuid().ToString();
            LocalClientInfo.ConnectionType = ConnectionTypes.Unknown;
            LocalClientInfo.ID = DateTime.Now.Ticks;

            var IPs = Dns.GetHostEntry(Dns.GetHostName()).AddressList.Where(ip => ip.AddressFamily == AddressFamily.InterNetwork);

            foreach (var IP in IPs)
            {
                LocalClientInfo.InternalAddresses.Add(IP);
            }
        }

        public void ConnectOrDisconnect()
        {
            if (_tCPClient.Connected)
            {
                _tCPClient.Client.Disconnect(true);
            }
            else
            {
                try
                {
                    _internetAccessAdapter = GetAdapterWithInternetAccess();

                    OnResultsUpdate?.Invoke(this, "Adapter with Internet Access: " + _internetAccessAdapter);

                    _tCPClient = new TcpClient();
                    _tCPClient.Client.Connect(ServerEndpoint);

                    UDPListen = true;
                    TCPListen = true;

                    SendMessageUDP(LocalClientInfo.Simplified(), ServerEndpoint);
                    LocalClientInfo.InternalEndpoint = (IPEndPoint)_uDPClient.Client.LocalEndPoint;

                    Thread.Sleep(500);
                    SendMessageTCP(LocalClientInfo);

                    var KeepAlive = new Thread(new ThreadStart(delegate
                    {
                        while (_tCPClient.Connected)
                        {
                            Thread.Sleep(5000);
                            SendMessageTCP(new KeepAlive());
                        }
                    }))
                    {
                        IsBackground = true
                    };
                    KeepAlive.Start();

                        OnServerConnect?.Invoke(this, new EventArgs());
                }
                catch (Exception ex)
                {
                    OnResultsUpdate?.Invoke(this, "Error when connecting " + ex.Message);
                }
            }
        }

        private IPAddress GetAdapterWithInternetAccess()
        {
            foreach (var netInterface in NetworkInterface.GetAllNetworkInterfaces())
            {
                if(netInterface.OperationalStatus != OperationalStatus.Up)
                {
                    var ipProps = netInterface.GetIPProperties();
                    foreach (var addr in ipProps.UnicastAddresses)
                    {
                        return addr.Address;
                    }
                }
            }

            return null;
        }

        public void SendMessageTCP(IP2PBase Item)
        {
            if (_tCPClient.Connected)
            {
                var Data = Item.ToByteArray();

                try
                {
                    var NetStream = _tCPClient.GetStream();
                    NetStream.Write(Data, 0, Data.Length);
                }
                catch (Exception e)
                {
                    OnResultsUpdate?.Invoke(this, "Error on TCP Send: " + e.Message);
                }
            }
        }

        public void SendMessageUDP(IP2PBase Item, IPEndPoint EP)
        {
            Item.ID = LocalClientInfo.ID;

            var data = Item.ToByteArray();

            try
            {
                if (data != null)
                {
                    _uDPClient.Send(data, data.Length, EP);
                }
            }
            catch (Exception e)
            {
                    OnResultsUpdate?.Invoke(this, "Error on UDP Send: " + e.Message);
            }
        }

        private void ListenUDP()
        {
            _threadUDPListen = new Thread(new ThreadStart(delegate
            {
                while (UDPListen)
                {
                    try
                    {
                        var EP = LocalClientInfo.InternalEndpoint;

                        if (EP != null)
                        {
                            var ReceivedBytes = _uDPClient.Receive(ref EP);
                            var Item = ReceivedBytes.ToP2PBase();
                            ProcessItem(Item, EP);
                        }
                    }
                    catch (Exception e)
                    {
                        OnResultsUpdate?.Invoke(this, "Error the UDP Receive: " + e.Message);
                    }
                }
            }))
            {
                IsBackground = true
            };

            if (UDPListen)
            {
                _threadUDPListen.Start();
            }
        }

        private void ListenTCP()
        {
            _threadTCPListen = new Thread(new ThreadStart(delegate
            {
                var ReceivedBytes = new byte[4096];
                var BytesRead = 0;

                while (TCPListen)
                {
                    try
                    {
                        if (!_tCPClient.Connected)
                        {
                            break;
                        }

                        BytesRead = _tCPClient.GetStream().Read(ReceivedBytes, 0, ReceivedBytes.Length);

                        if (BytesRead == 0)
                        {
                            break;
                        }
                        else
                        {
                            var Item = ReceivedBytes.ToP2PBase();
                            ProcessItem(Item);
                        }
                    }
                    catch (Exception e)
                    {
                        OnResultsUpdate?.Invoke(this, "Error on TCP Receive: " + e.Message);
                    }
                }
            }))
            {
                IsBackground = true
            };

            if (TCPListen)
            {
                _threadTCPListen.Start();
            }
        }

        private void ProcessItem(IP2PBase Item, IPEndPoint EP = null)
        {
            if(Item.GetType() == typeof(Data))
            {
                DataRecived?.Invoke((Data)Item,EP);
            }
            else if (Item.GetType() == typeof(Message))
            {
                var m = (Message)Item;
                var CI = _clients.FirstOrDefault(x => x.ID == Item.ID);

                if (m.ID == 0)
                {
                    OnResultsUpdate?.Invoke(this, m.From + ": " + m.Content);
                }

                if (m.ID != 0 & EP != null & CI != null)
                {
                    if (OnMessageReceived != null)
                    {
                        OnMessageReceived.Invoke(EP, new MessageReceivedEventArgs(CI, m, EP));
                    }
                }
            }
            else if (Item.GetType() == typeof(Notification))
            {
                var N = (Notification)Item;

                if (N.Type == NotificationsTypes.Disconnected)
                {
                    var CI = _clients.FirstOrDefault(x => x.ID == long.Parse(N.Tag.ToString()));

                    if (CI != null)
                    {
                        OnClientRemoved?.Invoke(this, CI);

                        _clients.Remove(CI);
                    }
                }
                else if (N.Type == NotificationsTypes.ServerShutdown)
                {
                    OnResultsUpdate?.Invoke(this, "Server shutting down.");

                    ConnectOrDisconnect();
                }
            }
            else if (Item.GetType() == typeof(Req))
            {
                var R = (Req)Item;

                var CI = _clients.FirstOrDefault(x => x.ID == R.ID);

                if (CI != null)
                {
                    OnResultsUpdate?.Invoke(this, "Received Connection Request from: " + CI.ToString());

                    var ResponsiveEP = FindReachableEndpoint(CI);

                    if (ResponsiveEP != null)
                    {

                            OnResultsUpdate?.Invoke(this, "Connection Successfull to: " + ResponsiveEP.ToString());
                        
                            OnClientConnection?.Invoke(CI, ResponsiveEP);
                        

                            OnClientUpdated?.Invoke(this, CI);
                        
                    }
                }
            }
            else if (Item.GetType() == typeof(Ack))
            {
                var A = (Ack)Item;

                if (A.Responce)
                {
                    _ackResponces.Add(A);
                }
                else
                {
                    var CI = _clients.FirstOrDefault(x => x.ID == A.ID);

                    if (CI.ExternalEndpoint.Address.Equals(EP.Address) & CI.ExternalEndpoint.Port != EP.Port)
                    {
                        OnResultsUpdate?.Invoke(this, "Received Ack on Different Port (" + EP.Port + "). Updating ...");

                        CI.ExternalEndpoint.Port = EP.Port;

                        OnClientUpdated?.Invoke(this, CI);
                    }

                    var IPs = new List<string>();
                    CI.InternalAddresses.ForEach(new Action<IPAddress>(delegate (IPAddress IP) { IPs.Add(IP.ToString()); }));

                    if (!CI.ExternalEndpoint.Address.Equals(EP.Address) & !IPs.Contains(EP.Address.ToString()))
                    {
                        OnResultsUpdate?.Invoke(this, "Received Ack on New Address (" + EP.Address + "). Updating ...");

                        CI.InternalAddresses.Add(EP.Address);
                    }

                    A.Responce = true;
                    A.RecipientID = LocalClientInfo.ID;
                    SendMessageUDP(A, EP);
                }
            }else if(Item.GetType() == typeof(ClientInfo))
            {
                Console.WriteLine("resived Client");
                _clients.Add((ClientInfo)Item);
                OnClientAdded?.Invoke(EP, (ClientInfo)Item);
                if (tokens.Contains(((ClientInfo)Item).ID))
                {
                    tokens.Remove(((ClientInfo)Item).ID);
                    ConnectToClient((ClientInfo)Item);
                }

            }
        }

        public List<long> tokens = new List<long>();

        public void ConnectToToken(string token)
        {
            tokens.Add(long.Parse(token));
            SendMessageTCP(new GetClient(long.Parse(token)));
        }

        public void ConnectToClient(ClientInfo CI)
        {
            var R = new Req(LocalClientInfo.ID, CI.ID);

            SendMessageTCP(R);

                OnResultsUpdate?.Invoke(this, "Sent Connection Request To: " + CI.ToString());


            var Connect = new Thread(new ThreadStart(delegate
            {
                var ResponsiveEP = FindReachableEndpoint(CI);

                if (ResponsiveEP != null)
                {
                    OnResultsUpdate?.Invoke(this, "Connection Successfull to: " + ResponsiveEP.ToString());

                    
                        OnClientConnection?.Invoke(CI, ResponsiveEP);
                    
                }
            }))
            {
                IsBackground = true
            };

            Connect.Start();
        }

        private IPEndPoint FindReachableEndpoint(ClientInfo CI)
        {
            OnResultsUpdate?.Invoke(this, "Attempting to Connect via LAN");

            for (var ip = 0; ip < CI.InternalAddresses.Count; ip++)
            {
                if (!_tCPClient.Connected)
                {
                    break;
                }

                var IP = CI.InternalAddresses[ip];

                var EP = new IPEndPoint(IP, CI.InternalEndpoint.Port);

                for (var i = 1; i < 4; i++)
                {
                    if (!_tCPClient.Connected)
                    {
                        break;
                    }

                    OnResultsUpdate?.Invoke(this, "Sending Ack to " + EP.ToString() + ". Attempt " + i + " of 3");

                    SendMessageUDP(new Ack(LocalClientInfo.ID), EP);
                    Thread.Sleep(200);

                    var Responce = _ackResponces.FirstOrDefault(a => a.RecipientID == CI.ID);

                    if (Responce != null)
                    {
                            OnResultsUpdate?.Invoke(this, "Received Ack Responce from " + EP.ToString());
                     

                        CI.ConnectionType = ConnectionTypes.LAN;

                        _ackResponces.Remove(Responce);

                        return EP;
                    }
                }
            }

            if (CI.ExternalEndpoint != null)
            {
                OnResultsUpdate?.Invoke(this, "Attempting to Connect via Internet");

                for (var i = 1; i < 100; i++)
                {
                    if (!_tCPClient.Connected)
                    {
                        break;
                    }

                        OnResultsUpdate?.Invoke(this, "Sending Ack to " + CI.ExternalEndpoint + ". Attempt " + i + " of 99");

                    SendMessageUDP(new Ack(LocalClientInfo.ID), CI.ExternalEndpoint);
                    Thread.Sleep(300);

                    var Responce = _ackResponces.FirstOrDefault(a => a.RecipientID == CI.ID);

                    if (Responce != null)
                    {
                            OnResultsUpdate?.Invoke(this, "Received Ack New from " + CI.ExternalEndpoint.ToString());
                    
                        CI.ConnectionType = ConnectionTypes.WAN;

                        _ackResponces.Remove(Responce);

                        return CI.ExternalEndpoint;
                    }
                }

                OnResultsUpdate?.Invoke(this, "Connection to " + CI.Name + " failed");
            }
            else
            {
                OnResultsUpdate?.Invoke(this, "Client's External EndPoint is Unknown");
            }

            return null;
        }
    }

    public class MessageReceivedEventArgs : EventArgs
    {
        public Message Message { get; set; }
        public ClientInfo ClientInfo { get; set; }
        public IPEndPoint EstablishedEP { get; set; }

        public MessageReceivedEventArgs(ClientInfo _clientInfo, Message _message, IPEndPoint _establishedEP)
        {
            ClientInfo = _clientInfo;
            Message = _message;
            EstablishedEP = _establishedEP;
        }
    }
}
