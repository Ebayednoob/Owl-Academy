using System;
using System.Numerics;
using SHDCCP;

var protocol = new SHDCcpProtocol();
var points = new Vector3[10];
for (int i = 0; i < 10; i++)
{
    double t = (double)i / 10 * 2 * Math.PI;
    points[i] = QuaternionKnot.Position(t);
}

byte[] packet = protocol.ConvertShapeToProtocolPacket(points);
Console.WriteLine($"Packet length: {packet.Length}");
Console.WriteLine($"First 20 bytes: {BitConverter.ToString(packet[..20])}");

// Try to deserialize directly
var serializer = new BitstreamSerializer();
try
{
    int streamId;
    bool hasInverse;
    var result = serializer.Deserialize(packet, out streamId, out hasInverse);
    Console.WriteLine($"Deserialize succeeded: {result.Count} items, streamId={streamId}, hasInverse={hasInverse}");
}
catch (Exception ex)
{
    Console.WriteLine($"Deserialize error: {ex.Message}");
}
