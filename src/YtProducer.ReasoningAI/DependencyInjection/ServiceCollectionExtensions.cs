using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using YtProducer.ReasoningAI.Abstractions;
using YtProducer.ReasoningAI.Providers.KieAi;

namespace YtProducer.ReasoningAI.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddReasoningAI(this IServiceCollection services, IConfiguration configuration)
    {
        services
            .AddOptions<KieAiOptions>()
            .Bind(configuration.GetSection(KieAiOptions.SectionName))
            .Configure(options =>
            {
                options.ApiKey = configuration["YT_PRODUCER_KIE_AI_API_KEY"] ?? options.ApiKey;
                options.BaseUrl = configuration["YT_PRODUCER_KIE_AI_BASE_URL"] ?? options.BaseUrl;
                options.DefaultModel = configuration["YT_PRODUCER_KIE_AI_MODEL"] ?? options.DefaultModel;

                if (int.TryParse(configuration["YT_PRODUCER_KIE_AI_TIMEOUT_SECONDS"], out var timeoutSeconds) && timeoutSeconds > 0)
                {
                    options.TimeoutSeconds = timeoutSeconds;
                }
            });

        services.AddHttpClient<KieAiReasoningClient>((serviceProvider, client) =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<KieAiOptions>>().Value;
            client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
        });

        services.AddScoped<IReasoningClientFactory, ReasoningClientFactory>();
        services.AddScoped<IReasoningClient>(serviceProvider => serviceProvider.GetRequiredService<KieAiReasoningClient>());

        return services;
    }

    private sealed class ReasoningClientFactory : IReasoningClientFactory
    {
        private readonly IServiceProvider _serviceProvider;

        public ReasoningClientFactory(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public IReasoningClient GetClient(ReasoningProvider provider)
        {
            return provider switch
            {
                ReasoningProvider.KieAi => _serviceProvider.GetRequiredService<KieAiReasoningClient>(),
                _ => throw new ReasoningClientException(provider, $"No reasoning client registered for provider {provider}.")
            };
        }
    }
}
