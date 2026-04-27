using System.Threading.Tasks;
using Flowingly_NewIntegration.Models;

namespace Flowingly_NewIntegration.IService
{
    public interface IExtractData
    {
        Task<OutputData[]> ExtractAsync(string input);
    }
}