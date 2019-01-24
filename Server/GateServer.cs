﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Net;
using Msg;
using Google.Protobuf;
using Proto;
using System.Reflection;

// LoginReq, LoginRes
// MatchReq, MatchRes
// UDPMoveStart, UDPMoveEnd, UDPChangeDir, EndGameReq, EndGameRes

namespace TestFrameSync
{
    class UserToken
    {
        public byte[] _Buffer;

        public UserToken()
        {
            _Buffer = new byte[1024 * 1024 * 2];
        }
    }

    class GateServer
    {
        public Dictionary<TcpClient, UserToken> _Clients = new Dictionary<TcpClient, UserToken>();

        // the maximum packet is 2 m. the packet whose size is out of the maximum will not be processed. 
        TcpListener _Server;
        public void Start()
        {
            _Server = new TcpListener(new IPEndPoint(IPAddress.Parse("127.0.0.1"), 8000));
            try
            {
                _Server.Start();
                Console.WriteLine("start server! ip address=" + _Server.Server.LocalEndPoint);
                _Server.BeginAcceptTcpClient(AcceptCallback, _Server);
            }
            catch (Exception ex)
            {
                Console.WriteLine("ex=" + ex.Message);
            }
        }

        void AcceptCallback(IAsyncResult result)
        {
            var server = result.AsyncState as TcpListener;
            try
            {
                var client = server.EndAcceptTcpClient(result);
                try
                {
                    UserToken token = new UserToken();
                    AddClient(client, token);
                    client.GetStream().BeginRead(token._Buffer, 0, token._Buffer.Length, ReceiveCallback, client);
                    Console.WriteLine("[INFO]client connect! ip address=" + client.Client.RemoteEndPoint);
                    server.BeginAcceptTcpClient(AcceptCallback, server);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("ex=" + ex.Message);
                    Disconnect(client);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("ex=" + ex.Message);
            }
        }

        void ReceiveCallback(IAsyncResult result)
        {
            var client = result.AsyncState as TcpClient;
            try
            {
                int receivedSize = client.GetStream().EndRead(result);
                if (receivedSize == 0)
                {
                    Disconnect(client);
                    return;
                }
                var token = GetClient(client);
                if (receivedSize <= token._Buffer.Length)
                {
                    ProcessReceivedMessage(token._Buffer.Take(receivedSize).ToArray(), client);
                    client.GetStream().BeginRead(token._Buffer, 0, token._Buffer.Length, ReceiveCallback, client);
                }
                else
                {
                    Disconnect(client);
                    Console.WriteLine("received size is out of range! ");
                }
            }
            catch (Exception ex)
            {
                Disconnect(client);
                Console.WriteLine("ex=" + ex.Message);
            }
        }

        public void Stop()
        {
            if (_Server == null)
            {
                return;
            }
            try
            {
                _Server.Stop();
            }
            catch (Exception ex)
            {
                Console.WriteLine("ex=" + ex.Message);
            }
            finally
            {
                _Server = null;
            }
        }

        public void Send<T>(T message, TcpClient client)
            where T : IMessage
        {
            if (client == null)
            {
                return;
            }
            BaseMessage m = new BaseMessage();
            var type = typeof(T);
            Console.WriteLine("send data type=" + type);
            m.Id = ProtoDic.GetProtoIdByProtoType(type);
            byte[] bytes = message.ToByteArray();
            m.Data = ByteString.CopyFrom(bytes);
            try
            {
                bytes = m.ToByteArray();
                client.GetStream().Write(bytes, 0, bytes.Length);
            }
            catch (Exception ex)
            {
                Console.WriteLine("ex=" + ex.Message);
            }
        }

        public UserToken GetClient(TcpClient client)
        {
            UserToken data = null;
            _Clients.TryGetValue(client, out data);
            return data;
        }

        void AddClient(TcpClient client, UserToken data)
        {
            if (!_Clients.ContainsKey(client))
            {
                _Clients.Add(client, data);
            }
        }

        void RemoveClient(TcpClient client)
        {
            if (_Clients.ContainsKey(client))
            {
                _Clients.Remove(client);
            }
        }

        void ProcessReceivedMessage(byte[] bytes, TcpClient client)
        {
            // parse to base message. 
            var message = BaseMessage.Parser.ParseFrom(bytes);

            // get target message info. 
            var type = ProtoDic.GetProtoTypeByProtoId(message.Id);
            Console.WriteLine("received data type=" + type);
            MessageParser messageParser = ProtoDic.GetMessageParser(type.TypeHandle);

            // convert to target message object. 
            object target = messageParser.ParseFrom(message.Data);

            List<Action<object, TcpClient>> list = null;
            if (_MessageCallbacks.TryGetValue(type, out list))
            {
                if (list == null || list.Count == 0)
                {
                    Console.WriteLine("list == null || list.Count == 0");
                    return;
                }

                for (int i = 0, length = list.Count; i < length; i++)
                {
                    list[i](target, client);
                }
            }
        }

        public Action<object, TcpClient> ConvertToObjAction<T>(Action<T, TcpClient> tAction)
            where T : IMessage
        {
            if (tAction == null)
            {
                return null;
            }
            else
            {
                return new Action<object, TcpClient>((obj, client) => tAction((T)obj, client));
            }
        }

        Dictionary<Type, List<Action<object, TcpClient>>> _MessageCallbacks = new Dictionary<Type, List<Action<object, TcpClient>>>();

        public void AddCallback<T>(Action<T, TcpClient> callback)
            where T : IMessage
        {
            var type = typeof(T);
            if (!_MessageCallbacks.ContainsKey(type))
            {
                var list = new List<Action<object, TcpClient>>();
                _MessageCallbacks.Add(type, list);
            }
            _MessageCallbacks[type].Add(ConvertToObjAction(callback));
        }

        public void RemoveCallback<T>(Action<T, TcpClient> callback)
            where T : IMessage
        {
            var type = typeof(T);
            List<Action<object, TcpClient>> list = null;
            if (_MessageCallbacks.TryGetValue(type, out list))
            {
                var call = ConvertToObjAction(callback);
                if (list.Contains(call))
                {
                    list.Remove(call);
                }
            }
        }

        public void Disconnect(TcpClient client)
        {
            if (_Clients.ContainsKey(client))
            {
                Console.WriteLine("[INFO]client disconnect! ip address=" + client.Client.RemoteEndPoint);
                _Clients.Remove(client);
                client.Close();
            }
        }
    }
}
