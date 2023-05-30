using System;
using System.IO;

using Xunit;

namespace Sdcb.FFmpeg.Tests.Toolboxs
{
    public class MediaMergeTest
    {
        [Theory]
        [InlineData(@"D:\华智信\项目\039\测试音视频\港片MV0.H264",
        @"D:\华智信\项目\039\测试音视频\港片MV1.H264",
        @"D:\华智信\项目\039\测试音视频\港片MV2.H264",
        @"D:\华智信\项目\039\测试音视频\港片MV3.H264",
        @"D:\华智信\项目\039\测试音视频\港片MV4.H264",
        @"D:\华智信\项目\039\测试音视频\ff-16b-1c-44100hz0.wma",
        @"D:\华智信\项目\039\测试音视频\ff-16b-1c-44100hz1.wma",
        @"D:\华智信\项目\039\测试音视频\ff-16b-1c-44100hz2.wma",
        @"D:\华智信\项目\039\测试音视频\ff-16b-1c-44100hz3.wma",
        @"D:\华智信\项目\039\测试音视频\ff-16b-1c-44100hz4.wma")]
        public async void RunTest(params string[] args)
        {
            var video = new AVStreams<string>(args[0], args[1], args[2], args[3], args[4]);
            var audio=new AVStreams<string>(args[5], args[6], args[7], args[8], args[9]);
            var context = MediaMergeTools.GetContext(video, audio);
            string savepath =Path.Combine(Environment.CurrentDirectory ,"Test.mp4");
            await MediaMergeTools.Merge(context.videos, context.audios, savepath, (proc) => { 
            
            });
        }
    }
}
