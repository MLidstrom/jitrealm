using System;
using System.Net.Sockets;
using System.Text;
using System.Threading;

var client = new TcpClient("localhost", 4000);
var stream = client.GetStream();

// Read with timeout
var buffer = new byte[4096];
var result = new StringBuilder();

stream.ReadTimeout = 3000;

// Read initial welcome
Thread.Sleep(1000);
while (stream.DataAvailable)
{
    var read = stream.Read(buffer, 0, buffer.Length);
    result.Append(Encoding.UTF8.GetString(buffer, 0, read));
}

Console.WriteLine("=== Welcome Screen ===");
Console.WriteLine(result.ToString().Substring(0, Math.Min(500, result.Length)));

// Send 'c' for create
var msg = Encoding.UTF8.GetBytes("c\r\n");
stream.Write(msg, 0, msg.Length);
Thread.Sleep(500);

result.Clear();
while (stream.DataAvailable)
{
    var read = stream.Read(buffer, 0, buffer.Length);
    result.Append(Encoding.UTF8.GetString(buffer, 0, read));
}
Console.WriteLine("\n=== After 'c' ===");
Console.WriteLine(result.ToString());

client.Close();
