using System.Net.Sockets;
using System.Text;

Console.WriteLine("Connecting to localhost:4000...");

using var client = new TcpClient("localhost", 4000);
using var stream = client.GetStream();

var buffer = new byte[4096];

// Helper to read available data
string ReadAvailable()
{
    var result = new StringBuilder();
    Thread.Sleep(500); // Wait for data
    while (stream.DataAvailable)
    {
        var read = stream.Read(buffer, 0, buffer.Length);
        result.Append(Encoding.UTF8.GetString(buffer, 0, read));
    }
    return result.ToString();
}

// Helper to send command
void Send(string text)
{
    var msg = Encoding.UTF8.GetBytes(text + "\r\n");
    stream.Write(msg, 0, msg.Length);
}

// Read welcome
Console.WriteLine("\n=== Welcome Screen ===");
var welcome = ReadAvailable();
// Strip ANSI codes for readability
var clean = System.Text.RegularExpressions.Regex.Replace(welcome, @"\x1b\[[0-9;]*[a-zA-Z]", "");
Console.WriteLine(clean.Length > 800 ? clean[..800] + "..." : clean);

// Choose create
Console.WriteLine("\n=== Sending 'c' for create ===");
Send("c");
var response1 = ReadAvailable();
clean = System.Text.RegularExpressions.Regex.Replace(response1, @"\x1b\[[0-9;]*[a-zA-Z]", "");
Console.WriteLine(clean);

// Enter player name
Console.WriteLine("\n=== Sending player name 'ClaudeBot' ===");
Send("ClaudeBot");
var response2 = ReadAvailable();
clean = System.Text.RegularExpressions.Regex.Replace(response2, @"\x1b\[[0-9;]*[a-zA-Z]", "");
Console.WriteLine(clean);

// Enter password
Console.WriteLine("\n=== Sending password ===");
Send("test123");
var response3 = ReadAvailable();
clean = System.Text.RegularExpressions.Regex.Replace(response3, @"\x1b\[[0-9;]*[a-zA-Z]", "");
Console.WriteLine(clean);

// Confirm password
Console.WriteLine("\n=== Confirming password ===");
Send("test123");
var response4 = ReadAvailable();
clean = System.Text.RegularExpressions.Regex.Replace(response4, @"\x1b\[[0-9;]*[a-zA-Z]", "");
Console.WriteLine(clean);

// Read final response
Thread.Sleep(1000);
var final = ReadAvailable();
clean = System.Text.RegularExpressions.Regex.Replace(final, @"\x1b\[[0-9;]*[a-zA-Z]", "");
Console.WriteLine("\n=== Final Response ===");
Console.WriteLine(clean.Length > 1000 ? clean[..1000] + "..." : clean);

// Send quit
Console.WriteLine("\n=== Sending 'quit' ===");
Send("quit");
Thread.Sleep(500);

Console.WriteLine("Done!");
