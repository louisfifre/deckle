using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Deckle.Notifications;
using Xunit;

namespace Deckle.Notifications.Tests;

// Unit: the dispatcher validates the descriptor against its catalogue, routes
// to the channel matching descriptor.Channel, and honours the null-on-drop
// contract. A fake channel stands in for the platform so the routing decisions
// are observable without a toast.
// Serialized with DeckleNotificationsSourceTests (shared collection): every
// Initialize call here emits on the shared provider the observability suite
// listens to.
[Collection("notification dispatcher singleton")]
[Trait("Category", "unit")]
public class NotificationDispatcherTests
{
    [Fact]
    public async Task PromptOnAnUnregisteredDescriptorThrowsInvalidOperationException()
    {
        var channel = new FakeNotificationChannel();
        var dispatcher = NotificationDispatcher.Initialize(channel);
        var descriptor = Descriptors.Make("playground.never_registered");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => dispatcher.PromptAsync(descriptor, ct: TestContext.Current.CancellationToken));

        Assert.Empty(channel.Calls);
    }

    [Fact]
    public async Task RegisteredDescriptorRoutesToTheMatchingChannelAndReturnsItsResponse()
    {
        var canned = new NotificationResponse("enroll", "typed text");
        var channel = new FakeNotificationChannel(cannedResponse: canned);
        var dispatcher = NotificationDispatcher.Initialize(channel);
        var descriptor = Descriptors.Make("playground.enroll");
        dispatcher.Catalog.Register(new[] { descriptor });

        var response = await dispatcher.PromptAsync(
            descriptor, ct: TestContext.Current.CancellationToken);

        Assert.Same(canned, response);
        var call = Assert.Single(channel.Calls);
        Assert.Equal(descriptor, call.Descriptor);
    }

    [Fact]
    public async Task BodyArgsArePassedThroughToTheChannel()
    {
        var channel = new FakeNotificationChannel();
        var dispatcher = NotificationDispatcher.Initialize(channel);
        var descriptor = Descriptors.Make("playground.with_args");
        dispatcher.Catalog.Register(new[] { descriptor });
        var args = new object?[] { "Notepad" };

        await dispatcher.PromptAsync(descriptor, args, TestContext.Current.CancellationToken);

        var call = Assert.Single(channel.Calls);
        Assert.Same(args, call.BodyArgs);
    }

    [Fact]
    public async Task AnUnavailableChannelReturnsNullWithoutPrompting()
    {
        var channel = new FakeNotificationChannel(isAvailable: false);
        var dispatcher = NotificationDispatcher.Initialize(channel);
        var descriptor = Descriptors.Make("playground.unavailable");
        dispatcher.Catalog.Register(new[] { descriptor });

        var response = await dispatcher.PromptAsync(
            descriptor, ct: TestContext.Current.CancellationToken);

        Assert.Null(response);
        Assert.Empty(channel.Calls);
    }

    [Fact]
    public async Task NoChannelForTheDescriptorPreferenceReturnsNull()
    {
        // The only channel claims Toast; nothing is registered for the
        // descriptor's preference, so the prompt drops to null.
        var dispatcher = NotificationDispatcher.Initialize();
        var descriptor = Descriptors.Make("playground.no_channel");
        dispatcher.Catalog.Register(new[] { descriptor });

        var response = await dispatcher.PromptAsync(
            descriptor, ct: TestContext.Current.CancellationToken);

        Assert.Null(response);
    }

    [Fact]
    public async Task CancellationPropagatesAsTaskCanceledException()
    {
        var channel = new FakeNotificationChannel
        {
            // Park the prompt so the token has a window to fire.
            PendingCompletion = new TaskCompletionSource<NotificationResponse>(),
        };
        var dispatcher = NotificationDispatcher.Initialize(channel);
        var descriptor = Descriptors.Make("playground.cancel");
        dispatcher.Catalog.Register(new[] { descriptor });

        using var cts = new CancellationTokenSource();
        var prompt = dispatcher.PromptAsync(descriptor, ct: cts.Token);
        cts.Cancel();

        await Assert.ThrowsAsync<TaskCanceledException>(() => prompt);
    }
}
