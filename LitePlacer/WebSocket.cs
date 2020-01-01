using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using WebSocketSharp;
using System.Threading;
//using websocket-sharp;
namespace LitePlacer
{
    public partial class WebSocket : UserControl
    {
        public event EventHandler<MessageEventArgs> received_data;
        //Uri ws = new Uri("ws://192.168.1.216:81");
        //ArraySegment<byte> SendSegment;
        //ArraySegment<byte> ReceiveSegment;
        byte[] receiveArray = new byte[200];
        byte[] sendArray = new byte[100];
        Thread dr;
        WebSocketSharp.WebSocket webs;
        Uri ws;
        bool busy = false;
        public bool IsOpen
        {
            get
            {
                //if (webs!= null && webs.ReadyState == WebSocketState.Open)
                if (webs != null && webs.IsAlive)
                    return true;
                else
                    return false;
            }
        }
        public WebSocket()
        {
            //webs = new WebSocketSharp.WebSocket("ws://192.168.1.181:81");
            //webs.OnMessage += Ws_OnMessage;
        }

       
        public bool WebSocketClose()
        {
            webs.Close();
            return true;
        }

        private void Ws_OnMessage(object sender, MessageEventArgs e)
        {

            if (e.Data != null)
                received_data(this,e);
        }

        public bool Connect(string ip)
        {
            if (ip == string.Empty)
                return false;
            //ws = new Uri("ws://" + ip + ":81");
           
            try
            {
                webs = new WebSocketSharp.WebSocket("ws://" + ip + ":81");
                webs.Connect();
                webs.OnMessage += Ws_OnMessage;
            }
            catch (Exception ex)
            {
                return false;
            }
            return true;

        }

       
        public bool WebSocketSend(string message)
        {

            try
            {
                if (IsOpen)
                {
                    webs.Send(message);
                }
                return true;
            }
            catch(Exception ex)
            {
                return false;
            }
        }

        

        

    }




}

