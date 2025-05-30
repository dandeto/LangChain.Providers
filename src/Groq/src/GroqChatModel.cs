﻿using GroqSharp.Models;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using MessageGroq = GroqSharp.Models.Message;

namespace LangChain.Providers.Groq;

public class GroqChatModel(
    GroqProvider provider,
    string id)
    : ChatModel(id), IChatModel
{
    private GroqProvider Provider { get; } = provider ?? throw new ArgumentNullException(nameof(provider));

    public override async IAsyncEnumerable<ChatResponse> GenerateAsync(
        ChatRequest request,
        ChatSettings? settings = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        request = request ?? throw new ArgumentNullException(nameof(request));
        var prompt = ToPrompt(request.Messages);
        Provider.Api.SetModel(Id);
        var watch = Stopwatch.StartNew();

        if (settings?.UseStreaming == true)
        {
            var responseStream = Provider.Api.CreateChatCompletionStreamAsync(prompt);
            await foreach (var response in responseStream.WithCancellation(cancellationToken))
            {
                yield return BuildChatResponse(response, request, settings, watch);
            }
        }
        else
        {
            var response = await Provider.Api.CreateChatCompletionAsync(prompt).ConfigureAwait(false);
            yield return BuildChatResponse(response, request, settings, watch);
        }

    }

    private ChatResponse BuildChatResponse(string response, ChatRequest request, ChatSettings? settings, Stopwatch watch)
    {
        var usage = Usage.Empty with
        {
            Time = watch.Elapsed,
        };

        var result = request.Messages.ToList();
        provider.AddUsage(usage);
        result.Add(response.AsAiMessage());

        return new ChatResponse
        {
            Messages = result,
            Usage = usage,
            UsedSettings = settings ?? ChatSettings.Default,
        };
    }

    private static MessageGroq[] ToPrompt(IEnumerable<Message> messages)
    {
        return messages.Select(ConvertMessage).ToArray();
    }

    private static MessageGroq ConvertMessage(Message message)
    {
        return new MessageGroq { Role = ConvertRole(message.Role), Content = message.Content };
    }

    private static MessageRoleType ConvertRole(MessageRole role)
    {
        return role switch
        {
            MessageRole.Human => MessageRoleType.User,
            MessageRole.Ai => MessageRoleType.Assistant,
            MessageRole.System => MessageRoleType.System,
            _ => throw new NotSupportedException($"the role {role} is not supported")
        };
    }
}