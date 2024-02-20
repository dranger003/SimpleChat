using System.Text;

namespace SimpleChat
{
    internal class Program
    {
        static async Task Main()
        {
            using var api = new API("<<YOUR_OPENAI_KEY_HERE>>");
            await Chat(api);
        }

        static async Task Chat(API api)
        {
            var role = String.Empty;
            var conversation = new List<ChatCompletionMessage>();
            var content = new StringBuilder();

            conversation.AddRange([
                new ChatCompletionMessage()
                {
                    Role = ChatCompletionRequestRole.System,
                    Content = $"You are a helpful assistant.",
                },
            ]);

            var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (s, e) => cts.Cancel(!(e.Cancel = true));

            while (true)
            {
                Console.Write($"Prompt (leave blank to quit): ");
                var input = Console.ReadLine();

                if (String.IsNullOrWhiteSpace(input))
                    break;

                conversation.Add(new ChatCompletionMessage() { Role = ChatCompletionRequestRole.User, Content = input });
                Console.WriteLine($"GPT (Ctrl+C to interrupt response):");

                await foreach (var result in api.CreateChatCompletion(conversation, 0.1, cancellationToken: cts.Token))
                {
                    if (result.Choices.Count > 0)
                    {
                        var choice = result.Choices.First();
                        var delta = choice.Delta;

                        if (delta.Role != null && delta.Role != role)
                            role = delta.Role;

                        content.Append(delta.Content);
                        Console.Write(delta.Content);
                    }
                }

                conversation.Add(new ChatCompletionMessage() { Role = Enum.Parse<ChatCompletionRequestRole>(role, true), Content = content.ToString() });
                content.Clear();

                if (cts.IsCancellationRequested) Console.Write(" [Cancelled]");

                Console.WriteLine("\n");

                cts = new();
            }
        }
    }
}
