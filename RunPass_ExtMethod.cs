using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Micros.Ops;
using Micros.PosCore.Extensibility;

namespace RunPass
{
    public partial class Application
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

        [ExtensibilityMethod]
        public void SpostaCorsa(object numCorsa)
        {
            if (!VerExtension()) return;
            if (!VerCourseMng()) return;

            var rigaTrovata = OpsContext.CheckDetail.FirstOrDefault(r => r.DetailLink == OpsContext.CurrentParentItem);
            if (rigaTrovata == null) return;

            CmdCambioCorsa(Convert.ToInt32(numCorsa));
        }

        [ExtensibilityMethod]
        public void VisCode()
        {
            OpsContext.ShowMessage(MessageSow(OpsContext.PropHierStrucID.ToString()));
        }

        [ExtensibilityMethod]
        public void TestComanda()
        {
            long? device;

            while (true)
            {
                device = OpsContext.RequestNumericEntry(
                    "Inserisci il numero del device per il test della comanda (1-32):",
                    "Test Comanda"
                );
                if (device >= 1 && device <= 32)
                {
                    break;
                }
                OpsContext.ShowMessage(
                    "Valore non valido. Inserire un numero compreso tra 1 e 32."
                );
            }
            int OrderDevice = device.HasValue ? (int)device.Value : 0;

            TestComanda(OrderDevice);
        }

        [ExtensibilityMethod]
        public void TestComanda(object OrderDevice)
        {
            if ((_device is null || ambiente.ActualRvc != OpsContext.RvcID)) LoadDevice();

            MyLog.Debug($"Inzio Stampa Comanda device {OrderDevice}");
            // DatiComanda datiComanda = PreparaDatiComadaTest();
            StampaComande(Convert.ToInt32(OrderDevice));
            MyLog.Debug($"Fine Stampa Comanda device {OrderDevice}");
        }

        [ExtensibilityMethod]
        public void MarciaNext()
        {
            if (!VerExtension()) return;
            if (!VerCourseMng()) return;
            if (!VerSequenceMode()) return;

            if (_gestioneCorse.SeTutteMarciate())
            {
                OpsContext.ShowMessage("Tuure le corse sono Marciate");
                return;
            }
            int? successiva = _gestioneCorse.NextMarcia();

            if (successiva != null && successiva.Value != 0)
                EseguiMarcia(successiva.Value);
            else
                OpsContext.ShowMessage("Le corse sono tutte marciate");
        }

        [ExtensibilityMethod]
        public void RepeatMarcia()
        {
            if (!VerExtension()) return;
            if (!VerCourseMng()) return;

            EseguiMarcia(_gestioneCorse.UltimaCorsaMarciataGlobale(), true);
        }

        [ExtensibilityMethod]
        public void Marcia(object numCorsa)
        {
            if (!VerExtension()) return;
            if (!VerCourseMng()) return;

            if (OpsContext.Check != null)
            {
                EseguiMarcia(Convert.ToInt32(numCorsa));
            }
            else
            {
                OpsContext.ShowMessage(MessageSow("Apri prima un conto!"));
            }


        }

        [ExtensibilityMethod]
        public void Status()
        {

            if (!VerExtension()) return;

            OpsContext.ShowMessage(MessageSow($"Versione Estesione{Assembly.GetExecutingAssembly().GetName().Version}\nScadenza {ambiente.MeseScadenza}/{ambiente.AnnoScadenza}\nGestione Corse abilitata {ambiente.Ce}\nGestione Menu abilitata {ambiente.Me}"));
        }

        [ExtensibilityMethod]
        public void ImpostaCorsaImmediata()
        {
            if (!VerExtension()) return;
            if (!VerCourseMng()) return;

            if (OpsContext.Check == null)
            {
                OpsContext.ShowMessage(MessageSow("Apri prima un conto!"));
                return;
            }
            int corsa = _gestioneCorse.CorsaAttuale();
            if (_gestioneCorse.SeCorsaMarciata(corsa))
                OpsContext.ShowMessage("Corsa già Marciata!");
            else
            {
                if (_gestioneCorse.SeCorsaInviata(corsa))
                    OpsContext.ShowMessage("Corsa già inviata!. Per renderla immediata devi dare il comando di Marcia");
                else
                    _gestioneCorse.AddMarciata(corsa);
            }
        }

        [ExtensibilityMethod]
        public void ImpostaCorsaASeguire()
        {
            if (!VerExtension()) return;
            if (!VerCourseMng()) return;

            if (OpsContext.Check == null)
            {
                OpsContext.ShowMessage(MessageSow("Apri prima un conto!"));
                return;
            }
            int corsa = _gestioneCorse.CorsaAttuale();
            if (!_gestioneCorse.SeCorsaMarciata(corsa))
                OpsContext.ShowMessage("Corsa non ancora Marciata!");
            else
            {
                if (_gestioneCorse.SeCorsaInviata(corsa))
                    OpsContext.ShowMessage("Corsa già inviata!.");
                else
                    _gestioneCorse.RemoveMarciata(corsa);
            }
        }

        [ExtensibilityMethod]
        public void SalvaCheck()
        {
            if (!VerExtension()) return;
            MyLog.Debug("SalvaCheck");
            if (!VerCourseMng()) return;

            AggiornaHold();

            if (_config.Testmode == 1)
                MyLog.Debug("Test Mode no stampa");
            else
                StampaComande();
            OpsCommand chiudiConto = new OpsCommand(OpsCommandType.TenderMedia)
            {
                Number = _config.TM_SendOrder
            };
            OpsContext.ProcessCommand(chiudiConto);

        }


        [ExtensibilityMethod]
        public void CancelCheck()
        {
            OpsContext.Check.ExtensibilityDetail.RemoveAll(this.ApplicationName);
            OpsCommand cmdVoidCeck = new OpsCommand(OpsCommandType.VoidCheck);
            OpsContext.ProcessCommand(cmdVoidCeck);
        }

        [ExtensibilityMethod]
        public void SplitCheck()
        {
            OpsContext.Check.ExtensibilityDetail.RemoveAll(this.ApplicationName);
            OpsCommand cmdVoidCeck = new OpsCommand(OpsCommandType.SplitCheck);
            OpsContext.ProcessCommand(cmdVoidCeck);
        }
    }
}
