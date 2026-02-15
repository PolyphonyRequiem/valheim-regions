using System;
using WorldZones.WorldGen;

var generator = new WorldGenerator("HHcLC5acQt");
var type = generator.GetType();
var offset0 = type.GetField("offset0", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
var offset1 = type.GetField("offset1", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
var offset2 = type.GetField("offset2", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
var offset3 = type.GetField("offset3", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
var offset4 = type.GetField("offset4", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

Console.WriteLine("WorldGenerator offsets for 'HHcLC5acQt':");
Console.WriteLine($"  offset0: {offset0.GetValue(generator)}");
Console.WriteLine($"  offset1: {offset1.GetValue(generator)}");
Console.WriteLine($"  offset2: {offset2.GetValue(generator)}");
Console.WriteLine($"  offset3: {offset3.GetValue(generator)}");
Console.WriteLine($"  offset4: {offset4.GetValue(generator)}");
