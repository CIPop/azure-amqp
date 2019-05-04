using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Amqp;

namespace TestAmqpClient
{
    interface IClient
    {
        string Status { get; }

        Task InitAsync();

        Task RunAsync();

        Task CleanUpAsync();
    }

    abstract class Client<T> : IClient where T : AmqpLink
    {
        protected static readonly Task CompletedTask;

        protected Options options;
        protected AmqpConnection connection;
        protected AmqpSession session;
        protected T link;
        long attempts;
        long success;
        long failure;

        static Client()
        {
#if NET452
            TaskCompletionSource<int> tcs = new TaskCompletionSource<int>();
            tcs.SetResult(0);
            CompletedTask = tcs.Task;
#else
            CompletedTask = Task.CompletedTask;
#endif
        }

        public Client(Options options)
        {
            this.options = options;
        }

        public string Status => $"success {this.success} failure {this.failure}";

        public async Task InitAsync()
        {
            AmqpConnectionFactory factory = new AmqpConnectionFactory();
            factory.TlsSettings.CertificateValidationCallback = (a, b, c, d) => true;
            factory.TlsSettings.CheckCertificateRevocation = false;
            factory.TlsSettings.Protocols = System.Security.Authentication.SslProtocols.Tls12;
            
            this.connection = await factory.OpenConnectionAsync(new Uri(this.options.Address), this.options.Sasl, TimeSpan.FromSeconds(30));
            this.session = this.connection.CreateSession(new AmqpSessionSettings());
            this.session.Opened += Session_Opened;
            this.session.Closed += Session_Closed;
           
            this.link = this.CreateLink();
            this.link.Opened += Link_Opened;
            this.link.Closed += Link_Closed;

            await Task.WhenAll(
                this.session.OpenAsync(this.session.DefaultOpenTimeout),
                this.link.OpenAsync(this.link.DefaultOpenTimeout));

            session.Abort();
        }

        private void Connection_Opened(object sender, EventArgs e)
        {
            throw new NotImplementedException();
        }

        private void Link_Closed(object sender, EventArgs e)
        {
            Console.WriteLine("Link closed");
        }

        private void Session_Closed(object sender, EventArgs e)
        {
            Console.WriteLine("Session closed");
        }

        private void Link_Opened(object sender, EventArgs e)
        {
            Console.WriteLine("Link opened");
        }

        private void Session_Opened(object sender, EventArgs e)
        {
            Console.WriteLine("Session opened");
        }

        public Task RunAsync()
        {
            Task[] tasks = new Task[this.options.Requests];
            for (long i = 0; i < this.options.Requests; i++)
            {
                tasks[i] = this.ExecuteAsync();
            }

            return Task.WhenAll(tasks);

        }

        public Task CleanUpAsync()
        {
            if (this.connection != null)
            {
                return this.connection.CloseAsync(this.connection.DefaultCloseTimeout);
            }

            return CompletedTask;
        }

        protected abstract T CreateLink();

        protected abstract Task ExecuteAsync();

        protected bool Attempt()
        {
            long temp = Interlocked.Increment(ref this.attempts);
            if (this.options.Progress > 0 && (temp % this.options.Progress) == 0)
            {
                Console.Out.WriteLine($"attempts {temp}");
            }

            return this.options.Count == 0 || temp <= this.options.Count;
        }

        protected void Success()
        {
            Interlocked.Increment(ref this.success);
        }

        protected void Failure()
        {
            Interlocked.Increment(ref this.failure);
        }
    }
}