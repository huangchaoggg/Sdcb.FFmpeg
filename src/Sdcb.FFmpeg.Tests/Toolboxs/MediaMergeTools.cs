using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Sdcb.FFmpeg.Codecs;
using Sdcb.FFmpeg.Formats;
using Sdcb.FFmpeg.Raw;
using Sdcb.FFmpeg.Toolboxs.Extensions;
using Sdcb.FFmpeg.Toolboxs.FilterTools;
using Sdcb.FFmpeg.Utils;

namespace Sdcb.FFmpeg.Tests.Toolboxs
{
    public class MediaMergeTools
    {
        public static (AVStreams<DecoderContext> videos, AVStreams<DecoderContext> audios,long duration) GetContext(AVStreams<string> videos,AVStreams<string> audios)
        {
            var video1 = CreateDecoderFrameQueue(videos.url1);
            var video2 = CreateDecoderFrameQueue(videos.url2);
            var video3 = CreateDecoderFrameQueue(videos.url3);
            var video4 = CreateDecoderFrameQueue(videos.url4);
            var video5 = CreateDecoderFrameQueue(videos.url5);
            var audio1 = CreateDecoderFrameQueue(audios.url1, AVMediaType.Audio);
            var audio2 = CreateDecoderFrameQueue(audios.url2, AVMediaType.Audio);
            var audio3 = CreateDecoderFrameQueue(audios.url3, AVMediaType.Audio);
            var audio4 = CreateDecoderFrameQueue(audios.url4, AVMediaType.Audio);
            var audio5 = CreateDecoderFrameQueue(audios.url5, AVMediaType.Audio);
            var duration = new List<long>(new[]
            {
                video1.InFc.Duration,
                video2.InFc.Duration,
                video3.InFc.Duration,
                video4.InFc.Duration,
                video5.InFc.Duration
            }).Max();
            return (new AVStreams<DecoderContext>(video1,video2,video3,video4,video5),
                new AVStreams<DecoderContext>(audio1,audio2,audio3,audio4,audio5), duration);
        }
        public static async Task Merge(AVStreams<DecoderContext> videos,
            AVStreams<DecoderContext> audios,string savePath, Action<long> Process)
        {
            Size size = new Size(1366, 768);
            AppositionFilter appositionFilter = AppositionFilter.AllocFilter(size, new[]
            {
                new AppositionParams(videos.url1.InFc.GetVideoStream(),new Rectangle(0, 0, 455, 384)),
                new AppositionParams(videos.url2.InFc.GetVideoStream(),new Rectangle(456, 0, 455, 384)),
                new AppositionParams(videos.url3.InFc.GetVideoStream(),new Rectangle(913, 180, 455, 384)),
                new AppositionParams(videos.url4.InFc.GetVideoStream(),new Rectangle(0, 385, 455, 384)),
                new AppositionParams(videos.url5.InFc.GetVideoStream(),new Rectangle(456, 385, 455, 384))
            });

            var outAudioFormat = new AudioSinkParams(GetChannelLayout(2), audios.url1.InFc.GetAudioStream().Codecpar!.SampleRate, AVSampleFormat.Fltp);
            AmixFilter amixFilter = AmixFilter.AllocFilter(outAudioFormat, new[]
           {
                new AudioSinkParams(audios.url1.InFc.GetAudioStream().Codecpar!.ChLayout,
                audios.url1.InFc.GetAudioStream().Codecpar!.SampleRate,
                (AVSampleFormat)audios.url1.InFc.GetAudioStream().Codecpar!.Format),

                new AudioSinkParams(audios.url2.InFc.GetAudioStream().Codecpar!.ChLayout,
                audios.url2.InFc.GetAudioStream().Codecpar!.SampleRate,
                (AVSampleFormat)audios.url2.InFc.GetAudioStream().Codecpar!.Format),

                new AudioSinkParams(audios.url3.InFc.GetAudioStream().Codecpar!.ChLayout,
                audios.url3.InFc.GetAudioStream().Codecpar!.SampleRate,
                (AVSampleFormat)audios.url3.InFc.GetAudioStream().Codecpar!.Format),

                new AudioSinkParams(audios.url4.InFc.GetAudioStream().Codecpar!.ChLayout,
                audios.url4.InFc.GetAudioStream().Codecpar!.SampleRate,
                (AVSampleFormat)audios.url4.InFc.GetAudioStream().Codecpar!.Format),

                new AudioSinkParams(audios.url5.InFc.GetAudioStream().Codecpar!.ChLayout,
                audios.url5.InFc.GetAudioStream().Codecpar!.SampleRate,
                (AVSampleFormat)audios.url5.InFc.GetAudioStream().Codecpar!.Format)
            });

            FormatContext outFc = FormatContext.AllocOutput(fileName: savePath);
            outFc.VideoCodec = Codec.CommonEncoders.Libx264;
            MediaStream outVideoStream = outFc.NewStream(outFc.VideoCodec);
            CodecContext videoEncoder = new CodecContext(outFc.VideoCodec)
            {
                Width = size.Width,
                Height = size.Height,
                TimeBase = new AVRational(1, 30),
                PixelFormat = AVPixelFormat.Yuv420p,
                Flags = AV_CODEC_FLAG.GlobalHeader,
                ThreadCount = Environment.ProcessorCount - 1,
            };
            videoEncoder.Open(outFc.VideoCodec, null);
            outVideoStream.Codecpar!.CopyFrom(videoEncoder);
            outVideoStream.TimeBase = videoEncoder.TimeBase;

            outFc.AudioCodec = Codec.CommonEncoders.AAC;
            MediaStream outAudioStream = outFc.NewStream(outFc.AudioCodec);
            CodecContext audioEncoder = new(outFc.AudioCodec)
            {
                ChLayout = outAudioFormat.ChLayout,
                SampleFormat = outFc.AudioCodec.Value.NegociateSampleFormat(outAudioFormat.SampleFormat),
                SampleRate = outFc.AudioCodec.Value.NegociateSampleRates(outAudioFormat.SampleRate),
                BitRate = outAudioFormat.SampleRate
            };

            audioEncoder.TimeBase = new AVRational(1,1000);
            audioEncoder.Open(outFc.AudioCodec);
            outAudioStream.Codecpar!.CopyFrom(audioEncoder);
            // begin write
            IOContext io = IOContext.OpenWrite(savePath);
            outFc.Pb = io;
            MediaDictionary pairs = new MediaDictionary()
            {
                ["movflags"] = "rtphint+faststart"
            };//流化
            outFc.WriteHeader(pairs);
            Dictionary<int, PtsDts> ptsDts = new Dictionary<int, PtsDts>();
            //BlockingCollection<Packet> queue = new BlockingCollection<Packet>(64);
            Task t1 = Task.Run(() =>
            {

                foreach (var p in amixFilter.WriteFrame(audios.url1.Queue, audios.url2.Queue, audios.url3.Queue, audios.url4.Queue, audios.url5.Queue)
                    .ToFrames()
                    .ConvertFrames(audioEncoder)
                    .AudioFifo(audioEncoder)
                    .EncodeFrames(audioEncoder))
                {
                    p.StreamIndex = outAudioStream.Index;
                    outFc.InterleavedWritePacket(p);
                    //queue.Add(p.Clone());
                }
                foreach (var p in appositionFilter.WriteFrame(videos.url1.Queue, videos.url2.Queue, videos.url3.Queue, videos.url4.Queue, videos.url5.Queue)
                .ToFrames()
                .ConvertFrames(videoEncoder)
                .ComputePtsDts(videoEncoder)
                .EncodeFrames(videoEncoder)
                .RecordPtsDts(ptsDts))
                {
                    p.StreamIndex = outVideoStream.Index;
                    outFc.InterleavedWritePacket(p);
                    //queue.Add(p.Clone());
                }
            });
            CancellationTokenSource tokenSource = new CancellationTokenSource();
            var t4 = Task.Run(async () =>
            {
                var token = tokenSource.Token;
                long lastValue = 0;
                while (!token.IsCancellationRequested)
                {
                    if (!ptsDts.ContainsKey(0))
                        ptsDts.Add(0, new PtsDts());
                    var durn = ptsDts[0].Pts;
                    long proc = durn - lastValue;
                    Process(proc);
                    lastValue = durn;
                    await Task.Delay(1000);
                }
            });
            await t1;
            tokenSource.Cancel();
            outFc.WriteTrailer();
            outFc.Flush();
        }
        private unsafe static AVChannelLayout GetChannelLayout(int nb_channels)
        {
            AVChannelLayout layout = new AVChannelLayout();
            ffmpeg.av_channel_layout_default(&layout, nb_channels);
            return layout;
        }
        private static DecoderContext CreateDecoderFrameQueue(string url, AVMediaType mediaType=AVMediaType.Video)
        {
            FormatContext inFc = FormatContext.OpenInputUrl(url);
            inFc.LoadStreamInfo();

            // prepare input stream/codec
            MediaStream stream = inFc.FindBestStream(mediaType);
            CodecContext decoder = new(Codec.FindDecoderById(stream.Codecpar!.CodecId));
            decoder.FillParameters(stream.Codecpar);
            decoder.Open();

            MediaThreadQueue<Frame> mediaThreadQueue = inFc
            .ReadPackets(stream.Index)
            .DecodePackets(decoder)
            .ToThreadQueue();
            return new(mediaThreadQueue, inFc);
        }
    }
    
    public record DecoderContext(MediaThreadQueue<Frame> Queue, FormatContext InFc);
    public record AVStreams<T>(T url1, T url2, T url3, T url4, T url5);
}
