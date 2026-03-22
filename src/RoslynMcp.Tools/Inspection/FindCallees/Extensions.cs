using Microsoft.Extensions.DependencyInjection;

namespace RoslynMcp.Tools.Inspection.FindCallees;

internal static class Extensions
{
	extension(IServiceCollection services)
	{
		public IServiceCollection AddFindCalleesTool() => services
			.AddSingleton<Service>()
			.AddSingleton<Tool>();
	}
}
