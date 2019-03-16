using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace TaskParallelLibrary.DataflowPipeline
{
    class Program
    {
        static readonly HttpClient Client = new HttpClient();
        static readonly HashAlgorithm HashAlgorithm = SHA1.Create();

        static async Task Main(string[] args)
        {
            var executionOptions = new ExecutionDataflowBlockOptions
            {
                MaxDegreeOfParallelism = Environment.ProcessorCount
            };

            var loader = new TransformBlock<string, string>(LoadPage, executionOptions);

            var searcher = new TransformManyBlock<string, Uri>(SearchForReferences, executionOptions);

            var fetcher = new TransformBlock<Uri, byte[]>(FetchReferences, executionOptions);

            var hasher = new TransformBlock<byte[], byte[]>(HashAlgorithm.ComputeHash, executionOptions);

            var converter = new TransformBlock<byte[], string>(TransformHashToHex, executionOptions);

            var printer = new ActionBlock<string>(PrintHash);

            var linkOptions = new DataflowLinkOptions
            {
                PropagateCompletion = true
            };

            loader.LinkTo(searcher, linkOptions, content => content is object);
            loader.LinkTo(DataflowBlock.NullTarget<string>());

            searcher.LinkTo(fetcher, linkOptions, uri => uri.Scheme == "https" || uri.Scheme == "http");
            searcher.LinkTo(DataflowBlock.NullTarget<Uri>());

            fetcher.LinkTo(hasher, linkOptions, content => content is object);
            fetcher.LinkTo(DataflowBlock.NullTarget<byte[]>());

            hasher.LinkTo(converter, linkOptions);

            converter.LinkTo(printer, linkOptions);

            var pages = new string[]
            {
                "https://arstechnica.com/",
                "https://www.reddit.com/",
                "https://www.anandtech.com/",
                "https://stigvoss.dk"
            };

            foreach (var page in pages)
            {
                loader.Post(page);
            }

            loader.Complete();

            await printer.Completion;
        }

        private static void PrintHash(string hash)
        {
            Console.WriteLine(hash);
        }

        private static string TransformHashToHex(byte[] hash)
        {
            return BitConverter.ToString(hash).Replace("-", "").ToLower();
        }

        private static async Task<string> LoadPage(string page)
        {
            try
            {
                return await Client.GetStringAsync(page);
            }
            catch (Exception)
            {
                return default;
            }
        }

        private static async Task<byte[]> FetchReferences(Uri url)
        {
            try
            {
                return await Client.GetByteArrayAsync(url);
            }
            catch (Exception)
            {
                return default;
            }
        }

        private static IEnumerable<Uri> SearchForReferences(string content)
        {
            const string Pattern = "src=\"(?<url>.+?)\"";
            var matches = Regex.Matches(content, Pattern,
                RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.IgnoreCase);

            return matches.Select(match => match.Groups["url"].Value)
                .Where(url => Uri.IsWellFormedUriString(url, UriKind.Absolute))
                .Select(url => new Uri(url));
        }
    }
}

