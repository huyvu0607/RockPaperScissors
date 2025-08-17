using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

class Program
{
    static Dictionary<TcpClient, int> clients = new Dictionary<TcpClient, int>();
    static Dictionary<TcpClient, string> choices = new Dictionary<TcpClient, string>();
    static List<Tuple<TcpClient, TcpClient>> activePairs = new List<Tuple<TcpClient, TcpClient>>();
    static Queue<TcpClient> waitingClients = new Queue<TcpClient>();
    static Dictionary<TcpClient, bool> readyAfterGame = new Dictionary<TcpClient, bool>();
    static object lockObj = new object();
    static int nextClientNumber = 1;

    static void Main()
    {
        TcpListener server = new TcpListener(IPAddress.Any, 8888);
        server.Start();
        Console.WriteLine("Server started...");

        while (true)
        {
            TcpClient client = server.AcceptTcpClient();

            lock (lockObj)
            {
                clients[client] = nextClientNumber++;
                waitingClients.Enqueue(client);
                Console.WriteLine($"Client {clients[client]} connected.");
                TryPairClients();
            }

            new Thread(HandleClient).Start(client);
        }
    }

    static void TryPairClients()
    {
        while (waitingClients.Count >= 2)
        {
            TcpClient c1 = waitingClients.Dequeue();
            TcpClient c2 = waitingClients.Dequeue();

            activePairs.Add(new Tuple<TcpClient, TcpClient>(c1, c2));

            // Gửi tên chính mình
            SendMessage(c1, $"NAME:Player {clients[c1]}");
            SendMessage(c2, $"NAME:Player {clients[c2]}");

            // Gửi tên đối thủ
            SendMessage(c1, $"OPPONENT:Player {clients[c2]}");
            SendMessage(c2, $"OPPONENT:Player {clients[c1]}");

            // Báo hiệu sẵn sàng
            SendMessage(c1, "READY");
            SendMessage(c2, "READY");

        }
    }


    static void HandleClient(object obj)
    {
        TcpClient client = (TcpClient)obj;
        NetworkStream stream = client.GetStream();
        byte[] buffer = new byte[1024];

        try
        {
            while (true)
            {
                int byteCount = stream.Read(buffer, 0, buffer.Length);
                if (byteCount == 0) break;

                string choice = Encoding.UTF8.GetString(buffer, 0, byteCount).Trim();
                Console.WriteLine($"Client {clients[client]} sent: {choice}");

                lock (lockObj)
                {
                    // 👉 Nếu client chọn READY sau 1 trận
                    if (choice == "READY")
                    {
                        readyAfterGame[client] = true;
                        var pair = activePairs.FirstOrDefault(p => p.Item1 == client || p.Item2 == client);

                        if (pair != null)
                        {
                            TcpClient other = pair.Item1 == client ? pair.Item2 : pair.Item1;

                            if (readyAfterGame.ContainsKey(other) && readyAfterGame[other])
                            {
                                // Cả 2 cùng READY → bỏ cặp cũ & cho vào hàng chờ
                                activePairs.Remove(pair);
                                readyAfterGame.Remove(client);
                                readyAfterGame.Remove(other);

                                waitingClients.Enqueue(client);
                                waitingClients.Enqueue(other);

                                TryPairClients();
                            }
                        }
                        continue; // Chờ lần gửi sau
                    }
                    else if (choice == "QUIT")
                    {
                        Console.WriteLine($"Client {clients[client]} quit the game.");
                        break;
                    }

                    // ✅ Nếu là Kéo/Búa/Bao → xử lý kết quả
                    choices[client] = choice;
                    var pairMove = activePairs.FirstOrDefault(p => p.Item1 == client || p.Item2 == client);

                    if (pairMove != null && choices.ContainsKey(pairMove.Item1) && choices.ContainsKey(pairMove.Item2))
                    {
                        string c1 = choices[pairMove.Item1];
                        string c2 = choices[pairMove.Item2];

                        string result1 = GetResult(c1, c2);
                        string result2 = GetResult(c2, c1);

                        SendMessage(pairMove.Item1, $"You chose {c1}, Opponent chose {c2} → {result1}");
                        SendMessage(pairMove.Item2, $"You chose {c2}, Opponent chose {c1} → {result2}");

                        choices.Remove(pairMove.Item1);
                        choices.Remove(pairMove.Item2);

                        SendMessage(pairMove.Item1, "Do you want to play again? Send READY or QUIT.");
                        SendMessage(pairMove.Item2, "Do you want to play again? Send READY or QUIT.");
                    }
                }
            }

        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error with Client {clients.GetValueOrDefault(client)}: {ex.Message}");
        }
        finally
        {
            lock (lockObj)
            {
                int clientNum = clients.GetValueOrDefault(client);
                Console.WriteLine($"Client {clientNum} disconnected.");

                clients.Remove(client);
                choices.Remove(client);

                // Remove from waiting queue
                waitingClients = new Queue<TcpClient>(waitingClients.Where(c => c != client));

                // Remove from active pairs
                var pair = activePairs.FirstOrDefault(p => p.Item1 == client || p.Item2 == client);
                if (pair != null)
                {
                    TcpClient remaining = pair.Item1 == client ? pair.Item2 : pair.Item1;
                    SendMessage(remaining, "Opponent disconnected. You are back in the queue.");
                    activePairs.Remove(pair);
                    if (clients.ContainsKey(remaining)) waitingClients.Enqueue(remaining);
                    TryPairClients();
                }
            }
            stream.Close();
            client.Close();
        }
    }

    static void SendMessage(TcpClient client, string message)
    {
        byte[] msg = Encoding.UTF8.GetBytes(message + "\n");
        try
        {
            client.GetStream().Write(msg, 0, msg.Length);
        }
        catch
        {
            // Ignore
        }
    }

    static string GetResult(string p1, string p2)
    {
        if (p1 == p2) return "Draw";
        if ((p1 == "Rock" && p2 == "Scissors") ||
            (p1 == "Scissors" && p2 == "Paper") ||
            (p1 == "Paper" && p2 == "Rock"))
            return "Win";
        return "Lose";
    }
}

