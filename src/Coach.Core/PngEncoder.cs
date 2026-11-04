using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
namespace Coach.Core;
public static class PngEncoder
{
 public static byte[] Encode(byte[] rgba,int width,int height)
 {
  if(width<=0||height<=0||rgba.Length!=checked(width*height*4))throw new ArgumentException("Invalid RGBA image");
  using var output=new MemoryStream();output.Write(new byte[]{137,80,78,71,13,10,26,10});
  var header=new byte[13];BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(0,4),width);BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4,4),height);header[8]=8;header[9]=6;Chunk(output,"IHDR",header);
  using var packed=new MemoryStream();using(var z=new ZLibStream(packed,CompressionLevel.Fastest,true))for(int y=0;y<height;y++){z.WriteByte(0);z.Write(rgba,y*width*4,width*4);}
  Chunk(output,"IDAT",packed.ToArray());Chunk(output,"IEND",[]);return output.ToArray();
 }
 static void Chunk(Stream output,string type,byte[] data)
 {
  Span<byte> n=stackalloc byte[4];BinaryPrimitives.WriteInt32BigEndian(n,data.Length);output.Write(n);
  var tag=Encoding.ASCII.GetBytes(type);output.Write(tag);output.Write(data);uint crc=0xffffffff;
  foreach(var b in tag.Concat(data)){crc^=b;for(int i=0;i<8;i++)crc=(crc&1)!=0?(crc>>1)^0xedb88320:crc>>1;}
  BinaryPrimitives.WriteUInt32BigEndian(n,crc^0xffffffff);output.Write(n);
 }
}
