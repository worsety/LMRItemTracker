﻿using System;
using System.Collections.Generic;
using EyeOfTruth;
using System.Threading;
using LiveSplit.ComponentUtil;
using System.Xml.Linq;
using System.ComponentModel;
using System.IO;

namespace LMRItemTracker
{
    class Program
    {
        static Dictionary<string, string> remakenames = new Dictionary<string, string>();

        static Dictionary<string, string> loadnames(Stream namesXml)
        {
            Dictionary<string, string> tempnames = new Dictionary<string, string>();

            try
            {
                foreach (XElement item in XElement.Load(namesXml).Descendants("item"))
                    try
                    {
                        tempnames.Add(item.Attribute("id").Value, item.Attribute("value").Value);
                    }
                    catch (ArgumentException)
                    {
                        System.Console.WriteLine("\"{0}\" is already mapped to \"{1}\", duplicate line in names.xml is trying to also map it to \"{2}\"",
                            item.Attribute("id").Value, tempnames[item.Attribute("id").Value], item.Attribute("value").Value);
                    }
                return tempnames;
            }
            catch (System.IO.FileNotFoundException) { System.Console.WriteLine("Absent names.xml"); }
            catch (System.IO.IOException) { System.Console.WriteLine("Error reading names.xml"); }
            catch (System.Xml.XmlException) { System.Console.WriteLine("Malformed names.xml"); }
            return new Dictionary<string, string>();
        }

        static void changed(object cur, object old, string name, LMRItemTracker.LaMulanaItemTrackerForm laMulanaItemTrackerForm)
        {
            if (!name.StartsWith("byte-") && !name.StartsWith("word-"))
                return;

            string displayname;
            if (!remakenames.TryGetValue(name, out displayname))
                return;


            string format = "";
            if (cur is byte || cur is sbyte)
                format = ":x2";
            else if (cur is ushort || cur is short)
                format = ":x4";
            else if (cur is uint || cur is int)
                format = ":x8";
            System.Console.WriteLine("{0} {1,15} := {2" + format + "} to {3" + format + "}", name, displayname, old, cur);

            if(displayname.StartsWith("boss-"))
            {
                laMulanaItemTrackerForm.toggleBoss(displayname, (byte)cur >= (byte)3);
            }
            else if (displayname.Equals("rosetta-count"))
            {
                laMulanaItemTrackerForm.updateTranslationTablets((byte)cur);
            }
            else if (displayname.StartsWith("shield-"))
            {
                bool isAdd = false;
                if(cur is ushort)
                {
                    isAdd = (ushort)cur > (ushort)0;
                }
                else if(cur is short)
                {
                    isAdd = (short)cur > (short)0;
                }

                laMulanaItemTrackerForm.updateShield(displayname, isAdd);
            }
            else if (displayname.StartsWith("recent-"))
            {
                if ((byte)old < 2 && (byte)cur >= 2)
                {
                    laMulanaItemTrackerForm.UpdateLastItem("shield-" + displayname.Split('-')[1]);
                }
            }
            else if (displayname.StartsWith("invus-lamp"))
            {
                laMulanaItemTrackerForm.updateLampOfTime(displayname, (ushort)cur >= 1);
            }
            else if (displayname.Equals("mantra-amphisbaena"))
            {
                laMulanaItemTrackerForm.toggleMantra(displayname, (byte)cur >= (byte)1);
            }
            else if (displayname.StartsWith("mantra-"))
            {
                laMulanaItemTrackerForm.toggleMantra(displayname, (byte)cur >= 4);
            }
            else if (displayname.StartsWith("w-map-"))
            {
                if((byte)old < 2 && (byte)cur >= 2)
                {
                    laMulanaItemTrackerForm.toggleMap(displayname, true);
                    laMulanaItemTrackerForm.UpdateLastItem(displayname);
                }
                else if((byte)old >= 2 && (byte)cur < 2)
                {
                    laMulanaItemTrackerForm.toggleMap(displayname, false);
                }
            }
            else if (displayname.Equals("whip"))
            {
                // For some reason, this is getting read as ushort even though it's actually signed.
                if ((ushort)cur == 65535)
                {
                    laMulanaItemTrackerForm.toggleWhip(false);
                }
                else
                {
                    laMulanaItemTrackerForm.toggleWhip(true);
                }
            }
            else if (displayname.Equals("ankh-jewels") || displayname.StartsWith("ammo-"))
            {
                int ammoCount = 0;
                if (cur is ushort)
                {
                    ammoCount = (ushort)cur;
                }
                else if (cur is short)
                {
                    ammoCount = (short)cur;
                }

                laMulanaItemTrackerForm.setAmmoCount(displayname, ammoCount);
            }
            else if (displayname.Equals("invtr-grailfull") || displayname.Equals("invtr-grailbr"))
            {
                laMulanaItemTrackerForm.toggleGrail(displayname, (ushort)cur >= 1);
            }
            else if (displayname.StartsWith("w-"))
            {
                if ((byte)old < 2 && (byte)cur >= 2)
                {
                    laMulanaItemTrackerForm.toggleItem(displayname, true);
                    laMulanaItemTrackerForm.UpdateLastItem(displayname);
                }
                else if ((byte)old >= 2 && (byte)cur < 2)
                {
                    laMulanaItemTrackerForm.toggleItem(displayname, false);
                }
            }
        }

        static bool warnedaboutaccess = false;

        public static object DoStuff(LMRItemTracker.LaMulanaItemTrackerForm laMulanaItemTrackerForm, Stream namesXml)
        {
            LaMulanaRemake remake = new LaMulanaRemake();

            MemoryWatcherList.MemoryWatcherDataChangedEventHandler changerhandler =
                (MemoryWatcher w) => changed(w.Current, w.Old, w.Name, laMulanaItemTrackerForm);
            remake.vars.OnWatcherDataChanged += changerhandler;

            byte[] rbytes_old = new byte[0x1000], rbytes_new;
            byte[] rwords_old = new byte[510], rwords_new;
            remakenames = loadnames(namesXml);
            int startupCounter = 1;

            while (true)
            {
                DateTime sleeptarget = DateTime.UtcNow.AddMilliseconds(100);
                try
                {
                    if (remake.Attach())
                    {
                        // I knew that using the MemoryWatchers for over 4000 variables would be slow but god damn it's slow
                        // let's not do it
                        remake.vars.RemoveAll(x => x.Name.StartsWith("byte") || x.Name.StartsWith("word"));
                        remake.vars.UpdateAll(remake.proc);
                        rbytes_new = remake.readbytes();
                        rwords_new = remake.readwords();
                        if (rbytes_new[824] == 0)
                        {
                            // Player is dead or hasn't started a game.
                            laMulanaItemTrackerForm.setGameStarted(false);
                            startupCounter = 1;
                        }

                        if(startupCounter == 2)
                        {
                            laMulanaItemTrackerForm.setGameStarted(true);
                        }

                        for (int i = 100; i < 0x1000; i++)
                            if (rbytes_new[i] != rbytes_old[i])
                                try
                                {
                                    changed(rbytes_new[i], rbytes_old[i], String.Format("byte-{0:x3}", i), laMulanaItemTrackerForm);
                                }
                                catch (Exception ex)
                                {
                                    System.Console.WriteLine(ex.StackTrace);
                                }
                        for (int i = 0; i < 510; i += 2)
                        {
                            ushort oldval = BitConverter.ToUInt16(rwords_old, i);
                            ushort newval = BitConverter.ToUInt16(rwords_new, i);
                            if (newval != oldval)
                                try
                                {
                                    changed(newval, oldval, String.Format("word-{0:x3}", i >> 1), laMulanaItemTrackerForm);
                                }
                                catch (Exception ex)
                                {
                                    System.Console.WriteLine(ex.StackTrace);
                                }
                        }
                        rbytes_old = rbytes_new;
                        rwords_old = rwords_new;
                        if (startupCounter < 2)
                        {
                            ++startupCounter;
                        }
                    }
                }
                catch (Win32Exception e)
                {
                    if (e.NativeErrorCode == 5 && e.TargetSite.ToString().StartsWith("Microsoft.Win32.SafeHandles.SafeProcessHandle OpenProcess"))
                    {
                        if (!warnedaboutaccess)
                            System.Console.WriteLine("Unable to access LaMulanaWin.exe, please check the compatibility settings\n"
                                + "and uncheck \"Run this program as an administrator\" if it is checked.");
                        warnedaboutaccess = true;
                    }
                }
                catch (Exception ex)
                {
                    System.Console.WriteLine(ex.StackTrace);
                }

                TimeSpan sleeptime = sleeptarget - DateTime.UtcNow;
                if (sleeptime > TimeSpan.Zero)
                    Thread.Sleep(sleeptime);
            }
        }
    }
}
