using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

using Sdcb.FFmpeg.Common;
using Sdcb.FFmpeg.Raw;

using static Sdcb.FFmpeg.Raw.ffmpeg;

namespace MediaPlayer.Extension
{
    public unsafe partial struct IndexEntity
    {
        private AVIndexEntry* _ptr;
        private IndexEntity(AVIndexEntry* ptr)
        {
            if (ptr == null)
            {
                throw new ArgumentNullException(nameof(ptr));
            }
            _ptr = ptr;
        }
        public static implicit operator AVIndexEntry*(IndexEntity? data)
        => data.HasValue ? (AVIndexEntry*)data.Value._ptr : null;
        public static IndexEntity FromNative(AVIndexEntry* ptr) => new IndexEntity(ptr);
        public long Pos=> _ptr->pos;
        public long Timestamp=>_ptr->timestamp;
        public int Flags2_size30=>_ptr->flags2_size30;
        public int Min_distance=>_ptr->min_distance;
    }
}
