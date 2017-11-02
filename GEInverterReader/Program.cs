using System;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;

namespace GEInverterReader
{
    class Program
    {
        private Object threadLock = new Object();

        public sealed class PVOutputParams : ICloneable
        {
            public string APIKey { get; set; }
            public string SystemID { get; set; }
            public string PVOutputUrl { get; set; }
            public string StringDate { get; set; }
            public string StringTime { get; set; }
            public int TotalPower { get; set; }
            public float TotalVolt { get; set;  }
            public int DataSetSize { get; set; }

            public object Clone()
            {
                return (PVOutputParams)this.MemberwiseClone();
            }

            object ICloneable.Clone() { return Clone(); }
        }

        public static string CurlPostString(string url, string data, string header)
        {
            Process p = null;
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "curl",
                    Arguments = string.Format("-k {0} --data \"{1}\" -H \"{2}\"", url, data,header),
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = false,
                };

                p = Process.Start(psi);
                Console.WriteLine(psi.Arguments);
                return p.StandardOutput.ReadToEnd();
            }
            finally
            {
                if (p != null && p.HasExited == false)
                    p.Kill();
            }
        }

        private static void SendToPVOutput(object state)
        {
            Console.WriteLine("Timer hit!");
            PVOutputParams param;
            lock (state)
            {
                param = (PVOutputParams)((PVOutputParams)state).Clone();
                ((PVOutputParams)state).DataSetSize = 0;
                ((PVOutputParams)state).TotalPower = 0;
            }

           
            if (param.DataSetSize > 0)
            {
                int totalPower = (param.TotalPower / param.DataSetSize);


                var datastring = param.StringDate + "," + totalPower;
                Console.WriteLine(datastring);

                var stopTrying = 1;
                while (stopTrying < 6)
                {
                    try
                    {
                        
                        var postData = "d=" + param.StringDate;
                        postData += "&t=" + param.StringTime;
                        postData += "&v2=" + totalPower;
                        postData += "&v6=" + param.TotalVolt.ToString("F0").Replace(",",".");
                        Console.WriteLine(postData);
                        /* Mono Debain workarround
                        var request = (HttpWebRequest)WebRequest.Create(param.PVOutputUrl);
                        request.Timeout = 15000;
                        var data = Encoding.ASCII.GetBytes(postData);
                        request.ContentType = "application/x-www-form-urlencoded";
                        request.ContentLength = data.Length;
                        request.Headers.Add("X-Pvoutput-Apikey: " + param.APIKey);
                        request.Headers.Add("X-Pvoutput-SystemId: " + param.SystemID);
                        request.Method = "POST";
                        request.CookieContainer = new CookieContainer();

                        ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
                        ServicePointManager.ServerCertificateValidationCallback += (p1, p2, p3, p4) => true;

                        using (var stream = request.GetRequestStream())
                        {
                            stream.Write(data, 0, data.Length);
                        }
                        var response = (HttpWebResponse)request.GetResponse();
                        var responseString = new StreamReader(response.GetResponseStream()).ReadToEnd();

                        */
                        var responseString = CurlPostString(param.PVOutputUrl,postData, "X-Pvoutput-Apikey: " + param.APIKey + Environment.NewLine+ "X-Pvoutput-SystemId: "+  param.SystemID );

                        Console.WriteLine("PVOutput Result: {0}", responseString);
                        if (!responseString.Contains("OK 200")) throw new Exception("Send PV Output Failed");
                        stopTrying = 10;
                        
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(ex.StackTrace);
                        Console.WriteLine("{2}/5: Failed to send data to {0}  Exception: {1}", param.PVOutputUrl, ex.Message, stopTrying);
                    }
                    finally
                    {
                        stopTrying++;
                    }
                }
            }
            else
            {
                Console.WriteLine("Nothing to send to PVOutput as dataset is 0 big");
            }
        }



        byte[] SendData(NetworkStream stream, byte[] sendData, bool response)
        {
            stream.ReadTimeout = 2000;
            stream.WriteTimeout = 2000;

            byte[] responseBuffer = new Byte[2048];
            stream.Write(sendData, 0, sendData.Length);

            if (response)
            {
                // String to store the response ASCII representation.

                Int32 bytes = stream.Read(responseBuffer, 0, 2048);

                byte[] newBuffer = new Byte[bytes];
                Array.Copy(responseBuffer, newBuffer, bytes);


                return newBuffer;
            }
            return null;

        }



        public static string[] ByteArrayToString(byte[] ba)
        {


            string hex = BitConverter.ToString(ba);

            string[] full = new string[2] { String.Empty, String.Empty };



            foreach (string letter in hex.Split('-'))
            {

                full[1] += " " + int.Parse(letter, System.Globalization.NumberStyles.HexNumber).ToString();
            }

            return full;
        }


        public static void Main(String[] args)
        {

            
            TcpClient tcpc = new TcpClient();
            Byte[] read = new Byte[32];
            NetworkStream stream = null;
            TcpClient client = null;

            int port = int.Parse(ConfigurationManager.AppSettings["GEInverterPort"]);
            string server = ConfigurationManager.AppSettings["GEInverterIP"];

            bool connected = false;

            // Buffer to store the response bytes.
            byte[] responseBuffer = new Byte[256];

            //here is the message that I am sending
            byte[] dataConnect = new byte[] { 0x00, 0x06, 0x17, 0x00, 0x00, 0x03, 0xcd, 0xae };
            byte[] dataPVInfo = new byte[] { 0x01, 0x03, 0xc0, 0xb2, 0x00, 0x05, 0x19, 0xee };
            byte[] dataPVStringInfo = new byte[] { 0x01, 0x03, 0xc0, 0x20, 0x00, 0x10, 0x79, 0xcc };
            byte[] dataPVGeneralInfo = new byte[] { 0x01, 0x03, 0xc0, 0x30, 0x00, 0x03, 0x39, 0xc4 };
            byte[] dataPVStatusInfo = new byte[] { 0x01, 0x03, 0xc0, 0x00, 0x00, 0x14, 0x79, 0xc5 };

            PVOutputParams pvop = new PVOutputParams();
            pvop.DataSetSize = 0;
            pvop.TotalPower = 0;

            pvop.APIKey = ConfigurationManager.AppSettings["PVOutputAPIKey"];
            pvop.SystemID = ConfigurationManager.AppSettings["PVOutputSystemID"];
            pvop.PVOutputUrl = ConfigurationManager.AppSettings["PVOutputURL"];

            Program t = new Program();


            DateTime now = DateTime.Now;
            int additionalMinutes = 5 - now.Minute % 5;
            if (additionalMinutes == 0)
            {
                additionalMinutes = 5;
            }
            var nearestOnFiveMinutes = new DateTime(
                now.Year,
                now.Month,
                now.Day,
                now.Hour,
                now.Minute,
                0
            ).AddMinutes(additionalMinutes);
            TimeSpan timeToStart = nearestOnFiveMinutes.Subtract(now);
            TimeSpan tolerance = TimeSpan.FromSeconds(1);
            if (timeToStart < tolerance)
            {
                timeToStart = TimeSpan.Zero;
            }

     

            Console.WriteLine("Initial timer set at {0}", DateTime.Now.Add(timeToStart));
            var pvOutputTimer = new Timer(SendToPVOutput, pvop, timeToStart, TimeSpan.FromMinutes(5));


        StartLoop:
            try
            {
                do
                {
                    while (!Console.KeyAvailable)
                    {

                        if (!connected)
                        {
                            client = new TcpClient();
                            if (!client.ConnectAsync(server, port).Wait(1000))
                            {

                                throw new Exception("Timeout on connect reached. Probably offline..");

                            }

                            stream = client.GetStream();
                            // First set
                            t.SendData(stream, dataConnect, false);
                            connected = true;
                        }
                        String fulloutput;

                        fulloutput = ByteArrayToString((t.SendData(stream, dataPVStringInfo, true)))[1];
                        fulloutput += ByteArrayToString(t.SendData(stream, dataPVGeneralInfo, true))[1];
                        fulloutput += ByteArrayToString(t.SendData(stream, dataPVStatusInfo, true))[1];


                        float curA = float.Parse(fulloutput.Split(' ')[31]);
                        if (Int32.Parse(fulloutput.Split(' ')[30]) == 1) { curA += 256; };
                        float curB = float.Parse(fulloutput.Split(' ')[33]);
                        if (Int32.Parse(fulloutput.Split(' ')[32]) == 1) { curB += 256; };
                        float curT = float.Parse(fulloutput.Split(' ')[13]);
                        if (Int32.Parse(fulloutput.Split(' ')[12]) == 1) { curT += 256; };

                        curA = curA / 10;
                        curB = curB / 10;
                        curT = curT / 10;

                        float pwrA = float.Parse(fulloutput.Split(' ')[35]);
                        if (Int32.Parse(fulloutput.Split(' ')[34]) == 1) { pwrA += 256; };
                        float pwrB = float.Parse(fulloutput.Split(' ')[42]);
                        if (Int32.Parse(fulloutput.Split(' ')[41]) == 1) { pwrB += 256; };
                        float pwrT = float.Parse(fulloutput.Split(' ')[5]);
                        if (Int32.Parse(fulloutput.Split(' ')[4]) == 1) { pwrT += 256; };

                        pwrA = pwrA * 10;
                        pwrB = pwrB * 10;
                        pwrT = pwrT * 10;


                        float vltA = float.Parse(fulloutput.Split(' ')[27]);
                        if (Int32.Parse(fulloutput.Split(' ')[26]) == 1) { vltA = vltA + 256; };
                        float vltB = float.Parse(fulloutput.Split(' ')[29]);
                        if (Int32.Parse(fulloutput.Split(' ')[28]) == 1) { vltB = vltB + 256; };
                        float vltT = float.Parse(fulloutput.Split(' ')[7]);
                        if (Int32.Parse(fulloutput.Split(' ')[6]) == 1) { vltT = vltT + 256; };

                        string strDate = DateTime.Now.ToString("yyyyMMdd");
                        string strTime = DateTime.Now.ToString("HH:mm");
                        // Retrieve temp
                       

                        lock (pvop)
                        {
                            pvop.StringDate = strDate;
                            pvop.StringTime = strTime;
                            pvop.TotalVolt = vltT;
                            pvop.TotalPower += int.Parse(pwrT.ToString("F0"));
                            pvop.DataSetSize++;
                        } 

                        Console.WriteLine("Current datetime {0} and added {1} to position {2} now avg {3}", strDate + " " + strTime, pvop.TotalPower, pvop.DataSetSize - 1, pvop.TotalPower / (pvop.DataSetSize));
                        var rerefTimer = pvOutputTimer;
                        Thread.Sleep(1000);

                      

                    }



                } while (Console.ReadKey(true).Key != ConsoleKey.Escape);


                if (connected)
                {
                    // Close everything.
                    stream.Close();
                    client.Close();
                }



            }
            catch (Exception e)
            {

                if (client.Connected)
                {
                    client.Close();
                    if (stream != null)
                    {
                        stream.Close();
                    }
                }


                // This point we just wait 10 seconds and reconnect.
                Console.WriteLine(e.Message);
                //Console.WriteLine(e.StackTrace);

                Thread.Sleep(60000);
                connected = false;
                goto StartLoop;
            }
        }


    }
}
