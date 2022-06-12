/* 
This code was copied from https://github.com/dotnet/runtime/issues/36093#issuecomment-755501938
*/

using Microsoft.Extensions.Options;
using System.ComponentModel.DataAnnotations;

namespace RurouniJones.Telemachus.Configuration.Util
{
    public class RecursiveDataAnnotationValidateOptions<TOptions> : IValidateOptions<TOptions> where TOptions : class
    {
        private readonly DataAnnotationsValidator.DataAnnotationsValidator _validator = new();

        public RecursiveDataAnnotationValidateOptions(string name) => Name = name;

        public string Name { get; }

        public ValidateOptionsResult Validate(string name, TOptions options)
        {
            if (name != Name)
                return ValidateOptionsResult.Skip;

            var results = new List<ValidationResult>();
            if (_validator.TryValidateObjectRecursive(options, results))
                return ValidateOptionsResult.Success;

            var stringResult = results.Select(validationResult =>
                $"Configuration validation failed for '{string.Join(",", validationResult.MemberNames)}' with the error: '{validationResult.ErrorMessage}'."
            ).ToList();

            return ValidateOptionsResult.Fail(stringResult);
        }
    }
}
