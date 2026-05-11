using System.Runtime.InteropServices;
using Unity.Mathematics;

namespace Microdetail
{
    [StructLayout(LayoutKind.Sequential)]
    public struct MicrodetailProperties
    {
        public uint Tint;
        public float Lod;
        public uint Normal;
    }
}