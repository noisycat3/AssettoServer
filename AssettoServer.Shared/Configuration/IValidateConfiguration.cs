using FluentValidation;

namespace AssettoServer.Shared.Configuration;

public interface IValidateConfiguration<T> where T : IValidator
{
    
}
