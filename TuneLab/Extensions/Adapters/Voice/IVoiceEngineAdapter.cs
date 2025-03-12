using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Extensions.Voice;
using TuneLab.Foundation.DataStructures;
using TuneLab.Foundation.Property;
using TuneLab.SDK.Voice;

namespace TuneLab.Extensions.Adapters.Voice;

internal static class IVoiceEngineAdapter
{
    public static IVoiceEngine ToDomain(this IVoiceEngine_V1 v1)
    {
        return new IVoiceEngineAdapter_V1(v1);
    }

    class IVoiceEngineAdapter_V1(IVoiceEngine_V1 v1) : IVoiceEngine
    {
        public IReadOnlyOrderedMap<string, VoiceSourceInfo> VoiceInfos => throw new NotImplementedException();

        public IVoiceSource CreateVoiceSource(string id, IReadOnlyMap<string, IReadOnlyPropertyValue> properties)
        {
            throw new NotImplementedException();
        }

        public void Destroy()
        {
            throw new NotImplementedException();
        }

        public void Init(IReadOnlyMap<string, IReadOnlyPropertyValue> properties)
        {
            throw new NotImplementedException();
        }
    }
}
