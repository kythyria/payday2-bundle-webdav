using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.IO;

namespace PD2BundleDavServer
{
    public class SliceStream : Stream, IDisposable, IAsyncDisposable
    {
        private Stream Backing;
        private long Base;
        private long _length;

        public SliceStream(Stream backing, long basePosition, long length)
        {
            if(!backing.CanRead || !backing.CanSeek)
            {
                throw new ArgumentException("Backing streams must be readable and seekable.");
            }

            if(basePosition + length > backing.Length)
            {
                throw new ArgumentException("Slice must lie within the backing stream's data.");
            }

            Backing = backing;
            Base = basePosition;
            _length = length;

            backing.Position = basePosition;
        }

        public override bool CanRead => true;

        public override bool CanSeek => true;

        public override bool CanWrite => false;

        public override long Length => _length;

        public override long Position
        {
            get => Backing.Position - Base;
            set
            {
                var newpos = Base + value;
                if(newpos < Base || newpos > Base + _length)
                {
                    throw new ArgumentOutOfRangeException("value", "Position must be inside the slice.");
                }
                Backing.Position = newpos;
            }
        }

        public override void Flush()
        {
            Backing.Flush();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (offset + count > buffer.Length) throw new ArgumentException("Buffer slice does not fit in buffer.");
            if (Position + count >= Length) count = (int)Math.Max(Length - Position, 0);
            return Backing.Read(buffer, offset, count);
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            Position = origin switch
            {
                SeekOrigin.Begin => offset,
                SeekOrigin.Current => Position + offset,
                SeekOrigin.End => _length + offset,
                _ => throw new ArgumentException("Invalid SeekOrigin, how did you even manage to do that", "origin"),
            };
            return Position;
        }

        public override void SetLength(long value) => throw new NotImplementedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotImplementedException();

        protected override void Dispose(bool disposing)
        {
            Backing.Dispose();
            base.Dispose(disposing);
        }

        public override ValueTask DisposeAsync()
        {
            return Backing.DisposeAsync();
        }
    }
}
