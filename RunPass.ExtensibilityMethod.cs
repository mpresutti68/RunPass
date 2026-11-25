using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Data;
using System.Data.Common;
using System.Data.Odbc;
using System.Data.SqlClient;
using System.Diagnostics.Contracts;
using System.Diagnostics.Eventing.Reader;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Linq.Expressions;      
using System.Net.Sockets;
using System.Numerics;
using System.Reflection;
using System.Resources;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Remoting.Contexts;
using System.Runtime.Remoting.Messaging;
using System.Security.Policy;
using System.Text;
using System.Threading.Tasks;
using Google.Protobuf.WellKnownTypes;
using Micros.Core.Communication;
using Micros.DataStore;
using Micros.Ops;
using Micros.Ops.Extensibility;
using Micros.Ops.Input;
using Micros.PosCore.Extensibility;
using Micros.PosCore.Extensibility.DataStore;
using Micros.PosCore.Extensibility.Ops;
using Micros.PosCore.Extensibility.Printing;
using Micros.PosCore.OperaPosting.CSH;
using Micros.PosCore.Printing;
using Microsoft.PointOfService;
using Newtonsoft.Json;
using static System.Net.Mime.MediaTypeNames;
using static Google.Protobuf.Reflection.UninterpretedOption.Types;


namespace RunPass
{
    public class Application 
    {
        [ExtensibilityMethod]
        public void AumentaCorsa()
        {
            if (!VerExtension()) return;
            if (!VerCourseMng()) return;
            if (!VerSequenceMode()) return;

            if (OpsContext.Check != null)
            {
                _gestioneCorse.NextCorsa();

            }
            else
            {
                OpsContext.ShowMessage(MessageSow("Apri prima un conto!"));
            }
        }

        [ExtensibilityMethod]
        public void CambiaCorsa(object numCorsa)
        {
            if (!VerExtension()) return;
            if (!VerCourseMng()) return;

            if (_gestioneCorse.SeCorsaChiusa(Convert.ToInt32(numCorsa)) && _config.BloccaCorseChiuse == 1)
            {
                OpsContext.ShowMessage(MessageSow("Corsa Chiusa!!!"));
                return;
            }
            if (OpsContext.Check != null)
            {
                _gestioneCorse.SetCorsa(Convert.ToInt32(numCorsa));
            }
            else
            {
                OpsContext.ShowMessage(MessageSow("Apri prima un conto!"));
            }
        }
    }
}
