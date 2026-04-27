using System.Threading.Tasks;
using Flowingly_NewIntegration.Model;


namespace Flowingly_NewIntegration.IServices
{
    public interface IExtractData
    {
        Task<OutputData[]> ExtractAsync(string input, decimal taxRate);
    }
}
