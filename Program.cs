using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MQTTnet;
using MQTTnet.Client;

namespace EVNHES_Agent
{
    class Program
    {
        private const string MqttServer = "10.9.123.14";
        private const int MqttPort = 7525;
        private const string TopicControl = "evnhes/web/control";
        
        private static string AgentNodeId = ""; 
        private static string ScriptPath = @"C:\EVN_Monitor\reset.bat";

        static async Task Main(string[] args)
        {
            Console.Title = "EVNHES MQTT Agent Service";
            
            if (args.Length > 0)
            {
                AgentNodeId = args[0].ToUpper();
            }
            else
            {
                Console.Write("Nhập mã định danh máy (VD: EVNHES_1, EVNHES_2, EVNHES_3, EVNHES_4): ");
                AgentNodeId = Console.ReadLine()?.Trim().ToUpper();
            }

            if (string.IsNullOrEmpty(AgentNodeId))
            {
                Console.WriteLine("Mã máy không hợp lệ! Dừng chương trình.");
                return;
            }

            Console.WriteLine($"[INFO] Khởi động Agent cho node: {AgentNodeId}");
            Console.WriteLine($"[INFO] Đường dẫn script reset: {ScriptPath}");

            var factory = new MqttFactory();
            var mqttClient = factory.CreateMqttClient();

            var options = new MqttClientOptionsBuilder()
                .WithTcpServer(MqttServer, MqttPort)
                .WithClientId($"EVNHES_Agent_{AgentNodeId}_{Guid.NewGuid().ToString().Substring(0, 5)}")
                .WithCleanSession()
                .Build();

            mqttClient.ApplicationMessageReceivedAsync += e =>
            {
                string payload = Encoding.UTF8.GetString(e.ApplicationMessage.PayloadSegment);
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Nhận tin nhắn: {payload}");

                try
                {
                    using (JsonDocument doc = JsonDocument.Parse(payload))
                    {
                        if (doc.RootElement.TryGetProperty("target", out JsonElement targetElement))
                        {
                            string targetNode = targetElement.GetString();
                            
                            if (string.Equals(targetNode, AgentNodeId, StringComparison.OrdinalIgnoreCase) || 
                                string.Equals(targetNode, "ALL", StringComparison.OrdinalIgnoreCase))
                            {
                                ExecuteResetScript();
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ERROR] Lỗi phân tích JSON: {ex.Message}");
                }

                return Task.CompletedTask;
            };

            mqttClient.DisconnectedAsync += async e =>
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Mất kết nối MQTT. Đang thử kết nối lại sau 5s...");
                await Task.Delay(TimeSpan.FromSeconds(5));
                try { await mqttClient.ConnectAsync(options, CancellationToken.None); } catch { }
            };

            try
            {
                await mqttClient.ConnectAsync(options, CancellationToken.None);
                await mqttClient.SubscribeAsync(new MqttTopicFilterBuilder().WithTopic(TopicControl).Build());
                Console.WriteLine($"[SUCCESS] Đã kết nối MQTT Broker {MqttServer}:{MqttPort} thành công.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Không thể kết nối MQTT Broker: {ex.Message}");
            }

            Console.WriteLine("Agent đang chờ lệnh...");
            await Task.Delay(-1);
        }

        private static void ExecuteResetScript()
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ==> Nhận lệnh Reset! Bắt đầu chạy {ScriptPath}...");

            if (!File.Exists(ScriptPath))
            {
                Console.WriteLine($"[ERROR] File không tồn tại: {ScriptPath}");
                return;
            }

            try
            {
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c \"{ScriptPath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using (Process proc = Process.Start(psi))
                {
                    string output = proc.StandardOutput.ReadToEnd();
                    string error = proc.StandardError.ReadToEnd();
                    proc.WaitForExit();

                    Console.WriteLine($"[OUTPUT]:\n{output}");
                    if (!string.IsNullOrEmpty(error))
                    {
                        Console.WriteLine($"[STDERR]:\n{error}");
                    }
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ==> Hoàn tất Reset!");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Lỗi chạy file bat: {ex.Message}");
            }
        }
    }
}
