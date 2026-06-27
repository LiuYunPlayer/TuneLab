using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TuneLab.Foundation;

public interface ISubscriber<in T, in TFunction>
{
    void Subscribe(T observable, TFunction action);
    void Unsubscribe(T observable, TFunction action);
}
