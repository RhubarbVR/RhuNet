using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using RhuNetShared;
using System.Threading;
using System.Net.NetworkInformation;

namespace RhuNet
{
    public class RhuClient
    {
        private IPAddress InternetAccessAdapter;

        private TcpClient TCPClient = new TcpClient();
        private UdpClient UDPClient = new UdpClient();

        public ClientInfo LocalClientInfo = new ClientInfo();
        private List<ClientInfo> Clients = new List<ClientInfo>();
        private List<Ack> AckResponces = new List<Ack>();

        private Thread ThreadTCPListen;
        private Thread ThreadUDPListen;

        public event EventHandler<string> OnResultsUpdate;
        public event EventHandler<ClientInfo> OnClientAdded;
        public event EventHandler<ClientInfo> OnClientUpdated;
        public event EventHandler<ClientInfo> OnClientRemoved;
        public event EventHandler OnServerConnect;
        public event EventHandler<IPEndPoint> OnClientConnection;
        public event EventHandler<MessageReceivedEventArgs> OnMessageReceived;

        private bool _TCPListen = false;
        public bool TCPListen
        {
            get { return _TCPListen; }
            set
            {
                _TCPListen = value;
                if (value)
                    ListenTCP();
            }
        }

        private bool _UDPListen = false;
        public bool UDPListen
        {
            get { return _UDPListen; }
            set
            {
                _UDPListen = value;
                if (value)
                    ListenUDP();
            }
        }
        public IPEndPoint ServerEndpoint;
        public RhuClient(string ip,int port)
        {
            ServerEndpoint = new IPEndPoint(IPAddress.Parse(ip), port);
            if(Environment.OSVersion.Platform == PlatformID.Win32NT)
            {
                UDPClient.AllowNatTraversal(true);
                UDPClient.Client.SetIPProtectionLevel(IPProtectionLevel.Unrestricted);
            }

            UDPClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);

            LocalClientInfo.Name = System.Environment.MachineName;
            LocalClientInfo.ConnectionType = ConnectionTypes.Unknown;
            LocalClientInfo.ID = DateTime.Now.Ticks;

            var IPs = Dns.GetHostEntry(Dns.GetHostName()).AddressList.Where(ip => ip.AddressFamily == AddressFamily.InterNetwork);

            foreach (var IP in IPs)
                LocalClientInfo.InternalAddresses.Add(IP);

            ConnectOrDisconnect();
        }

        public void ConnectOrDisconnect()
        {
            if (TCPClient.Connected)
            {
                TCPClient.Client.Disconnect(true);
            }
            else
            {
                try
                {
                    InternetAccessAdapter = GetAdapterWithInternetAccess();

                    if (OnResultsUpdate != null)
                        OnResultsUpdate.Invoke(this, "Adapter with Internet Access: " + InternetAccessAdapter);

                    TCPClient = new TcpClient();
                    TCPClient.Client.Connect(ServerEndpoint);

                    UDPListen = true;
                    TCPListen = true;

                    SendMessageUDP(LocalClientInfo.Simplified(), ServerEndpoint);
                    LocalClientInfo.InternalEndpoint = (IPEndPoint)UDPClient.Client.LocalEndPoint;

                    Thread.Sleep(500);
                    SendMessageTCP(LocalClientInfo);

                    Thread KeepAlive = new Thread(new ThreadStart(delegate
                    {
                        while (TCPClient.Connected)
                        {
                            Thread.Sleep(5000);
                            SendMessageTCP(new KeepAlive());
                        }
                    }));

                    KeepAlive.IsBackground = true;
                    KeepAlive.Start();

                    if (OnServerConnect != null)
                        OnServerConnect.Invoke(this, new EventArgs());

                }
                catch (Exception ex)
                {
                    if (OnResultsUpdate != null)
                        OnResultsUpdate.Invoke(this, "Error when connecting " + ex.Message);
                }
            }
        }

        private IPAddress GetAdapterWithInternetAccess()
        {
            foreach (NetworkInterface netInterface in NetworkInterface.GetAllNetworkInterfaces())
            {
                if(netInterface.OperationalStatus != OperationalStatus.Up)
                {
                    IPInterfaceProperties ipProps = netInterface.GetIPProperties();
                    foreach (UnicastIPAddressInformation addr in ipProps.UnicastAddresses)
                    {
                        return addr.Address;
                    }
                }
            }

            return null;
        }

        public void SendMessageTCP(IP2PBase Item)
        {
            if (TCPClient.Connected)
            {
                byte[] Data = Item.ToByteArray();

                try
                {
                    NetworkStream NetStream = TCPClient.GetStream();
                    NetStream.Write(Data, 0, Data.Length);
                }
                catch (Exception e)
                {
                    if (OnResultsUpdate != null)
                        OnResultsUpdate.Invoke(this, "Error on TCP Send: " + e.Message);
                }
            }
        }

        public void SendMessageUDP(IP2PBase Item, IPEndPoint EP)
        {
            Item.ID = LocalClientInfo.ID;

            byte[] data = Item.ToByteArray();

            try
            {
                if (data != null)
                    UDPClient.Send(data, data.Length, EP);
            }
            catch (Exception e)
            {
                if (OnResultsUpdate != null)
                    OnResultsUpdate.Invoke(this, "Error on UDP Send: " + e.Message);
            }
        }

        private void ListenUDP()
        {
            ThreadUDPListen = new Thread(new ThreadStart(delegate
            {
                while (UDPListen)
                {
                    try
                    {
                        IPEndPoint EP = LocalClientInfo.InternalEndpoint;

                        if (EP != null)
                        {
                            byte[] ReceivedBytes = UDPClient.Receive(ref EP);
                            IP2PBase Item = ReceivedBytes.ToP2PBase();
                            ProcessItem(Item, EP);
                        }
                    }
                    catch (Exception e)
                    {
                        if (OnResultsUpdate != null)
                            OnResultsUpdate.Invoke(this, "Error on UDP Receive: " + e.Message);
                    }
                }
            }));

            ThreadUDPListen.IsBackground = true;

            if (UDPListen)
                ThreadUDPListen.Start();
        }

        private void ListenTCP()
        {
            ThreadTCPListen = new Thread(new ThreadStart(delegate
            {
                byte[] ReceivedBytes = new byte[4096];
                int BytesRead = 0;

                while (TCPListen)
                {
                    try
                    {
                        if (!TCPClient.Connected) break;
                        BytesRead = TCPClient.GetStream().Read(ReceivedBytes, 0, ReceivedBytes.Length);

                        if (BytesRead == 0)
                            break;
                        else
                        {
                            IP2PBase Item = ReceivedBytes.ToP2PBase();
                            ProcessItem(Item);
                        }
                    }
                    catch (Exception e)
                    {
                        if (OnResultsUpdate != null)
                            OnResultsUpdate.Invoke(this, "Error on TCP Receive: " + e.Message);
                    }
                }
            }));

            ThreadTCPListen.IsBackground = true;

            if (TCPListen)
                ThreadTCPListen.Start();
        }

        private void ProcessItem(IP2PBase Item, IPEndPoint EP = null)
        {
            if (Item.GetType() == typeof(Message))
            {
                Message m = (Message)Item;
                ClientInfo CI = Clients.FirstOrDefault(x => x.ID == Item.ID);

                if (m.ID == 0)
                    if (OnResultsUpdate != null)
                        OnResultsUpdate.Invoke(this, m.From + ": " + m.Content);

                if (m.ID != 0 & EP != null & CI != null)
                    if (OnMessageReceived != null)
                        OnMessageReceived.Invoke(EP, new MessageReceivedEventArgs(CI, m, EP));
            }
            else if (Item.GetType() == typeof(ClientInfo))
            {
                ClientInfo CI = Clients.FirstOrDefault(x => x.ID == Item.ID);

                if (CI == null)
                {
                    Clients.Add((ClientInfo)Item);

                    if (OnClientAdded != null)
                        OnClientAdded.Invoke(this, (ClientInfo)Item);
                }
                else
                {
                    CI.Update((ClientInfo)Item);

                    if (OnClientUpdated != null)
                        OnClientUpdated.Invoke(this, (ClientInfo)Item);
                }
            }
            else if (Item.GetType() == typeof(Notification))
            {
                Notification N = (Notification)Item;

                if (N.Type == NotificationsTypes.Disconnected)
                {
                    ClientInfo CI = Clients.FirstOrDefault(x => x.ID == long.Parse(N.Tag.ToString()));

                    if (CI != null)
                    {
                        if (OnClientRemoved != null)
                            OnClientRemoved.Invoke(this, CI);

                        Clients.Remove(CI);
                    }
                }
                else if (N.Type == NotificationsTypes.ServerShutdown)
                {
                    if (OnResultsUpdate != null)
                        OnResultsUpdate.Invoke(this, "Server shutting down.");

                    ConnectOrDisconnect();
                }
            }
            else if (Item.GetType() == typeof(Req))
            {
                Req R = (Req)Item;

                ClientInfo CI = Clients.FirstOrDefault(x => x.ID == R.ID);

                if (CI != null)
                {
                    if (OnResultsUpdate != null)
                        OnResultsUpdate.Invoke(this, "Received Connection Request from: " + CI.ToString());

                    IPEndPoint ResponsiveEP = FindReachableEndpoint(CI);

                    if (ResponsiveEP != null)
                    {
                        if (OnResultsUpdate != null)
                            OnResultsUpdate.Invoke(this, "Connection Successfull to: " + ResponsiveEP.ToString());

                        if (OnClientConnection != null)
                            OnClientConnection.Invoke(CI, ResponsiveEP);

                        if (OnClientUpdated != null)
                            OnClientUpdated.Invoke(this, CI);
                    }
                }
            }
            else if (Item.GetType() == typeof(Ack))
            {
                Ack A = (Ack)Item;

                if (A.Responce)
                    AckResponces.Add(A);
                else
                {
                    ClientInfo CI = Clients.FirstOrDefault(x => x.ID == A.ID);

                    if (CI.ExternalEndpoint.Address.Equals(EP.Address) & CI.ExternalEndpoint.Port != EP.Port)
                    {
                        if (OnResultsUpdate != null)
                            OnResultsUpdate.Invoke(this, "Received Ack on Different Port (" + EP.Port + "). Updating ...");

                        CI.ExternalEndpoint.Port = EP.Port;

                        if (OnClientUpdated != null)
                            OnClientUpdated.Invoke(this, CI);
                    }

                    List<string> IPs = new List<string>();
                    CI.InternalAddresses.ForEach(new Action<IPAddress>(delegate (IPAddress IP) { IPs.Add(IP.ToString()); }));

                    if (!CI.ExternalEndpoint.Address.Equals(EP.Address) & !IPs.Contains(EP.Address.ToString()))
                    {
                        if (OnResultsUpdate != null)
                            OnResultsUpdate.Invoke(this, "Received Ack on New Address (" + EP.Address + "). Updating ...");

                        CI.InternalAddresses.Add(EP.Address);
                    }

                    A.Responce = true;
                    A.RecipientID = LocalClientInfo.ID;
                    SendMessageUDP(A, EP);
                }
            }
        }

        public void ConnectToClient(ClientInfo CI)
        {
            Req R = new Req(LocalClientInfo.ID, CI.ID);

            SendMessageTCP(R);

            if (OnResultsUpdate != null)
                OnResultsUpdate.Invoke(this, "Sent Connection Request To: " + CI.ToString());

            Thread Connect = new Thread(new ThreadStart(delegate
            {
                IPEndPoint ResponsiveEP = FindReachableEndpoint(CI);

                if (ResponsiveEP != null)
                {
                    if (OnResultsUpdate != null)
                        OnResultsUpdate.Invoke(this, "Connection Successfull to: " + ResponsiveEP.ToString());

                    if (OnClientConnection != null)
                        OnClientConnection.Invoke(CI, ResponsiveEP);
                }
            }));

            Connect.IsBackground = true;

            Connect.Start();
        }

        private IPEndPoint FindReachableEndpoint(ClientInfo CI)
        {
            if (OnResultsUpdate != null)
                OnResultsUpdate.Invoke(this, "Attempting to Connect via LAN");

            for (int ip = 0; ip < CI.InternalAddresses.Count; ip++)
            {
                if (!TCPClient.Connected)
                    break;

                IPAddress IP = CI.InternalAddresses[ip];

                IPEndPoint EP = new IPEndPoint(IP, CI.InternalEndpoint.Port);

                for (int i = 1; i < 4; i++)
                {
                    if (!TCPClient.Connected)
                        break;

                    if (OnResultsUpdate != null)
                        OnResultsUpdate.Invoke(this, "Sending Ack to " + EP.ToString() + ". Attempt " + i + " of 3");

                    SendMessageUDP(new Ack(LocalClientInfo.ID), EP);
                    Thread.Sleep(200);

                    Ack Responce = AckResponces.FirstOrDefault(a => a.RecipientID == CI.ID);

                    if (Responce != null)
                    {
                        if (OnResultsUpdate != null)
                            OnResultsUpdate.Invoke(this, "Received Ack Responce from " + EP.ToString());

                        CI.ConnectionType = ConnectionTypes.LAN;

                        AckResponces.Remove(Responce);

                        return EP;
                    }
                }
            }

            if (CI.ExternalEndpoint != null)
            {
                if (OnResultsUpdate != null)
                    OnResultsUpdate.Invoke(this, "Attempting to Connect via Internet");

                for (int i = 1; i < 100; i++)
                {
                    if (!TCPClient.Connected)
                        break;

                    if (OnResultsUpdate != null)
                        OnResultsUpdate.Invoke(this, "Sending Ack to " + CI.ExternalEndpoint + ". Attempt " + i + " of 99");

                    SendMessageUDP(new Ack(LocalClientInfo.ID), CI.ExternalEndpoint);
                    Thread.Sleep(300);

                    Ack Responce = AckResponces.FirstOrDefault(a => a.RecipientID == CI.ID);

                    if (Responce != null)
                    {
                        if (OnResultsUpdate != null)
                            OnResultsUpdate.Invoke(this, "Received Ack New from " + CI.ExternalEndpoint.ToString());

                        CI.ConnectionType = ConnectionTypes.WAN;

                        AckResponces.Remove(Responce);

                        return CI.ExternalEndpoint;
                    }
                }

                if (OnResultsUpdate != null)
                    OnResultsUpdate.Invoke(this, "Connection to " + CI.Name + " failed");
            }
            else
            {
                if (OnResultsUpdate != null)
                    OnResultsUpdate.Invoke(this, "Client's External EndPoint is Unknown");
            }

            return null;
        }
    }

    public class MessageReceivedEventArgs : EventArgs
    {
        public Message message { get; set; }
        public ClientInfo clientInfo { get; set; }
        public IPEndPoint EstablishedEP { get; set; }

        public MessageReceivedEventArgs(ClientInfo _clientInfo, Message _message, IPEndPoint _establishedEP)
        {
            clientInfo = _clientInfo;
            message = _message;
            EstablishedEP = _establishedEP;
        }
    }
}
