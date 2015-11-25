﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace HLDJConverter
{
    public static class MediaConverter
    {
        public static Task FFmpegConvertToWavAsync(string srcFilepath, string dstFilepath, int dstFrequency, double dstVolume)
        {
            var process = new Process();
            process.EnableRaisingEvents = true;
            var processCompletionTask = new TaskCompletionSource<object>();
            process.Exited += async (s, a) =>
            {
                //Console.WriteLine(process.StandardOutput.ReadToEnd());
                //Console.WriteLine(process.StandardError.ReadToEnd());
                await Task.Run(async () => await FixFFmpegWavFileHeader(dstFilepath));
                processCompletionTask.SetResult(null);
            };

            process.StartInfo = new ProcessStartInfo
            {
                //RedirectStandardError = true,
                //RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                FileName = @"ffmpeg.exe",
                Arguments = $"-y -i \"{srcFilepath}\" -map_metadata -1 -codec:a pcm_s16le -q:a 100 -ac 1 -ar {dstFrequency.ToString()} -af \"volume={dstVolume:0.00}\" \"{dstFilepath}\"",
            };
            
            process.Start();

            return processCompletionTask.Task;
        }

#if Nreco
        public static async Task NRecoConvertToWavAsync(string srcFilepath, string dstFolder, int dstBitrate, double dstVolume, string dstFilename = null)
        {
            if(string.IsNullOrEmpty(dstFilename))
                dstFilename = Path.GetFileNameWithoutExtension(srcFilepath);
            
            dstFilename = RemoveInvalidFilenameCharacters(dstFilename);
            string destination = $"{dstFolder}\\{dstFilename}.wav";

            var converter = new FFMpegConverter();

            await Task.Run(() => converter.ConvertMedia(srcFilepath, "mp4", destination, "wav", new ConvertSettings()
            {
                AudioCodec = "pcm_s16le",
                AudioSampleRate = dstBitrate,
                CustomOutputArgs = "-map_metadata -1 -aq 100 -ac 1"
            }));

            await FixFFmpegWavFileHeader(destination);
        }
#endif

        public static string RemoveInvalidFilenameCharacters(string filename)
        {
            // Remove all invalid filename characters
            var result = Regex.Replace(filename, "[\\*/:<>?|\"]", "");

            // Remove all non-english characters. HLDJ can't properly parse them.
            result = Regex.Replace(result, @"[^a-zA-Z0-9% ._,'~#()\-\[\]]", "");

            // Remove any outlying spaces or hyphens
            return result.Trim(' ', '-');
        }

        public static string EnsureUniqueFilepath(string filepath)
        {
            if(!File.Exists(filepath))
                return filepath;

            string filename = Path.GetFileNameWithoutExtension(filepath);
            string extension = Path.GetExtension(filepath);
            string directory = Path.GetDirectoryName(filepath);
            for(int i = 1; File.Exists(filepath); ++i)
                filepath = $"{directory}\\{filename}({i.ToString()}){extension}";

            return filepath;
        }

        /// <summary>
        /// HLDJ cannot read wav files generated by ffmpeg because ofsome extra
        /// data in the wav file's header. This method removes that data.
        /// </summary>
        private static async Task FixFFmpegWavFileHeader(string filepath)
        {
            const int FFmpegHeaderSizeInBytes = 34;
            
            // Load the file into memory
            byte[] data;
            using(var file = File.OpenRead(filepath))
            {
                data = new byte[file.Length - FFmpegHeaderSizeInBytes];
                
                // Read the file into the data buffer, skipping over the FFmpeg header data.
                await file.ReadAsync(data, 0, 36);
                file.Seek(FFmpegHeaderSizeInBytes, SeekOrigin.Current);
                await file.ReadAsync(data, 36, (int)(file.Length - file.Position));
            }
            
            // Set the new file size
            byte[] filesize = BitConverter.GetBytes(data.Length);
            data[4] = filesize[0];
            data[5] = filesize[1];
            data[6] = filesize[2];
            data[7] = filesize[3];

            // Save
            File.WriteAllBytes(filepath, data);
        }
    }
}
