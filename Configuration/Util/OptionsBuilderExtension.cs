/* 
This code was copied from https://github.com/dotnet/runtime/issues/36093#issuecomment-755501938
*/

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace RurouniJones.Telemachus.Configuration.Util
{
    public static class OptionsBuilderExtension
    {
        public static OptionsBuilder<T> ValidateDataAnnotationsRecursively<T>(this OptionsBuilder<T> builder) where T : class
        {
            builder.Services.AddSingleton<IValidateOptions<T>>(new RecursiveDataAnnotationValidateOptions<T>(builder.Name));
            return builder;
        }
    }
}
