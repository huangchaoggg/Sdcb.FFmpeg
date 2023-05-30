﻿using Sdcb.FFmpeg.Codecs;
using Sdcb.FFmpeg.Formats;
using Sdcb.FFmpeg.Raw;
using Sdcb.FFmpeg.Utils;
using static Sdcb.FFmpeg.Raw.ffmpeg;

using System.Collections.Generic;

namespace Sdcb.FFmpeg.Toolboxs.Extensions
{
    public record struct PtsDts(long Pts, long Dts)
    {
        public bool HasValue => this != Default;
        public static PtsDts Default => new (ffmpeg.AV_NOPTS_VALUE, ffmpeg.AV_NOPTS_VALUE);
    }

    public static class PtsDtsExtensions
    {
        public static IEnumerable<Packet> RecordPtsDts(this IEnumerable<Packet> packets, Dictionary<int, PtsDts> packetTiming)
        {
            foreach (Packet packet in packets)
            {
                packetTiming[packet.StreamIndex] = new(packet.Pts, packet.Dts);
                yield return packet;
            }
        }
        public static IEnumerable<Frame> ComputePtsDts(this IEnumerable<Frame> frames,CodecContext videoCodec)
        {
            long frame_index=0,audioFrame_Pts=0;
            foreach (Frame frame in frames)
            {
                if (frame.Width > 0)
                {
                    //var Framerate = videoCodec.Framerate.Num==0|| videoCodec.Framerate.Den==0? videoCodec.TimeBase:videoCodec.Framerate;
                    var dura= (1 / av_q2d(videoCodec.TimeBase) / av_q2d(videoCodec.TimeBase));
                    var pts = frame_index++ * dura;
                    frame.PktDuration = (long)dura;
                    frame.Pts = (long) pts;
                    frame.PktDts = (long) pts;
                    yield return frame;
                }
                else if(frame.SampleRate > 0)
                {
                    frame.Pts = audioFrame_Pts;
                    frame.PktDts= audioFrame_Pts;
                    audioFrame_Pts += frame.NbSamples;
                    yield return frame;
                }
            }
        }
    }
}
