using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using MailKit;
using MailKit.Net.Imap;

namespace Milos
{
    enum WorkerExitCode
    {
        Found,
        NotFound,
        ConnectionError,
        AuthenticationError,
        UnknownError,
    }

    class Worker
    {
        public User Mail { get; set; }
        public List<Regex> Subjects { get; set; }
        public List<Regex> From { get; set; }
        public int MailCount { get; set; }
        public string OutFile { get; set; }

        public WorkerExitCode DoWork()
        {
            Console.WriteLine($"Starting worker for {Mail.EMail}");

            var client = new ImapClient();
            client.ServerCertificateValidationCallback = (sender, certificate, chain, errors) => true;
            try
            {
                client.Connect(Mail.Server.Hostname, Mail.Server.Port);
            }
            catch (Exception e)
            {
                return WorkerExitCode.ConnectionError;
            }

            try
            {
                client.Authenticate(Mail.EMail, Mail.Password);
            }
            catch (Exception e)
            {
                return WorkerExitCode.AuthenticationError;
            }

            var inbox = client.Inbox;
            inbox.Open(FolderAccess.ReadOnly);

            foreach (var summary in inbox.Fetch(Math.Max(0, inbox.Count - MailCount), inbox.Count - 1, MessageSummaryItems.Envelope))
            {
                foreach (var regex in Subjects)
                {
                    if (regex.IsMatch(summary.Envelope.Subject ?? ""))
                    {
                        Console.WriteLine($"Found match for {regex} in {Mail.EMail}:{Mail.Password}: {summary.Envelope.From.Mailboxes.First()} -> {summary.Envelope.Subject}");
                        return Finish();
                    }
                }

                foreach (var regex in From)
                {
                    if (summary.Envelope.From.Count > 0 && regex.IsMatch($"{summary.Envelope.From.Mailboxes.First().Name} {summary.Envelope.From.Mailboxes.First().Address}" ?? ""))
                    {
                        Console.WriteLine($"Found match for {regex} in {Mail.EMail}:{Mail.Password}: {summary.Envelope.From.Mailboxes.First()} -> {summary.Envelope.Subject}");
                        return Finish();
                    }
                }
            }

            return WorkerExitCode.NotFound;
        }

        private WorkerExitCode Finish()
        {
            File.AppendAllText(OutFile, $"{Mail.EMail}:{Mail.Password}");
            return WorkerExitCode.Found;
        }
    }
}
