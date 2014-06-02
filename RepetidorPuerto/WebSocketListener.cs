using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Collections.Generic;
using System.Security.Cryptography;
namespace RepetidorPuerto
{
    // State object for reading client data asynchronously
    public class StateObject
    {
        public Socket workSocket = null;
        public const int BufferSize = 1024;
        public byte[] buffer = new byte[BufferSize];
        public List<byte> Bbuffer = new List<byte>();
        public StringBuilder sb = new StringBuilder();
        public void initializeBuffer() {
            Bbuffer = new List<byte>();
            sb = new StringBuilder();
            buffer = new byte[BufferSize];
        }

        public void appendSB(Byte[] b, int bytesRead)
        {
            sb.Append(Encoding.UTF8.GetString(buffer, 0, bytesRead));
            for (int i = 0; i < bytesRead; i++)
            {
                Bbuffer.Add(buffer[i]);
            }
        }

    }

    public class WebSocketListener
    {
        public static ManualResetEvent allDone = new ManualResetEvent(false);
        public static String EOF = "\r\n";
        public static Dictionary<Socket,Dictionary<String,String>> clientes;
        public delegate void OnReceiveMessage(String function,Object []parameters,Socket cliente);
        public static OnReceiveMessage onReceiveMessage=null;

        public static void StartListening()
        {
            GC.Collect();
            clientes = new Dictionary<Socket,Dictionary<String,String>>();
            byte[] bytes = new Byte[1024];
            IPEndPoint localEndPoint = new IPEndPoint(IPAddress.Any, 60443);

            Socket listener = new Socket(AddressFamily.InterNetwork,
                SocketType.Stream, ProtocolType.Tcp);

            try
            {
                listener.Bind(localEndPoint);
                listener.Listen(100);

                while (true)
                {
                    allDone.Reset();

                    Console.WriteLine("Waiting for a connection...");
                    listener.BeginAccept(
                        new AsyncCallback(AcceptCallback),listener);
                    allDone.WaitOne();
                }

            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
            }

            Console.WriteLine("\nPress ENTER to continue...");

        }

        public static void AcceptCallback(IAsyncResult ar)
        {
            allDone.Set();
            Socket listener = (Socket)ar.AsyncState;
            Socket handler = listener.EndAccept(ar);
            clientes.Add(handler,new Dictionary<string,string>());
            StateObject state = new StateObject();
            state.workSocket = handler;
            handler.BeginReceive(state.buffer, 0, StateObject.BufferSize, 0, new AsyncCallback(ReadCallback), state);
        }

        public static void ReadCallback(IAsyncResult ar)
        {
            StateObject state = (StateObject)ar.AsyncState;
            Socket handler = state.workSocket;

            int bytesRead = handler.EndReceive(ar);

            if (bytesRead > 0)
            {
                state.appendSB(state.buffer, bytesRead);
                String aux = state.sb.ToString();
                if (clientes[handler].Count == 0)
                {
                    if (aux.IndexOf(EOF) > -1)
                    {
                        processData(state, handler);
                        state.initializeBuffer();
                        GC.Collect();
                    }
                }
                else {
                    processData(state, handler);
                    state.initializeBuffer();
                    GC.Collect();
                }
                handler.BeginReceive(state.buffer, 0, StateObject.BufferSize, 0, new AsyncCallback(ReadCallback), state);

            }
        }

        private static void processData(StateObject so,Socket handler)
        {
            String content = so.sb.ToString();
            if (clientes[handler].Count == 0)
            {
                Dictionary<String, String> data = clientes[handler];
                String[] Lines = content.Split('\n');

                foreach (String line in Lines)
                {
                    String[] vars = line.Split(':');
                    if (vars.Length > 1)
                    {
                        data.Add(vars[0].Trim(), vars[1].Trim());
                    }
                }

                if (data.ContainsKey("Sec-WebSocket-Key"))
                {
                    String swk = data["Sec-WebSocket-Key"] + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";
                    swk = Convert.ToBase64String(SHA1.Create().ComputeHash(Encoding.UTF8.GetBytes(swk)));
                    String response = "HTTP/1.1 101 Switching Protocols" + Environment.NewLine
                    + "Connection: Upgrade" + Environment.NewLine
                    + "Upgrade: websocket" + Environment.NewLine
                    + "Sec-WebSocket-Accept: " + swk + Environment.NewLine
                    + Environment.NewLine;
                    Send(handler, response);
                }
            }
            else {
                Byte[] data=decodeContent(so.Bbuffer.ToArray());
                if (data!=null) {
                    String message = Encoding.UTF8.GetString(data);
                    Console.WriteLine("Received: "+message);
                    String[] funcion = message.Split(',');
                    if (funcion.Length > 0 && onReceiveMessage!=null) { 
                        Queue<String> q=new Queue<string>(funcion);
                        q.Dequeue();
                        onReceiveMessage.Invoke(funcion[0], q.ToArray(), handler);
                    }
                }
            }
        }

        private static Byte[] decodeContent(Byte []data)
        {
            if(data.Length>0){
                int nframe = data[0]-128;
                int tama = data[1] - 128;
                int[] cryptData=new int[4];
                cryptData[0] = data[2];
                cryptData[1] = data[3];
                cryptData[2] = data[4];
                cryptData[3] = data[5];

                Byte[] decoded = new Byte[tama];
                for (int i = 0; i < tama; i++)
                {
                    decoded[i] = (Byte)(data[i+6] ^ cryptData[i % 4]);
                }
                return decoded;
            }
            return null;
        }

        public static void Send(Socket handler, String data)
        {
            byte[] byteData = Encoding.ASCII.GetBytes(data);

            handler.BeginSend(byteData, 0, byteData.Length, 0,
                new AsyncCallback(SendCallback), handler);
        }

        private static void SendCallback(IAsyncResult ar)
        {
            try
            {
                Socket handler = (Socket)ar.AsyncState;
                int bytesSent = handler.EndSend(ar);
                Console.WriteLine("Sent {0} bytes to client.", bytesSent);
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
            }
        }
    }
}