//////////////////////////////////////////////////////////////////////////////
//
// (c) 2010, Careminster Limited, Magne Metaverse Research and Thomas Grimshaw
//
// All Rights Reserved.
//

using System;
using System.Linq;
using System.IO;
using System.Reflection;
using System.Xml;
using System.Xml.Linq;
using System.Collections.Generic;
using System.Collections.Specialized;

using Nini.Config;
using log4net;
using OpenMetaverse;
using OpenSim.Framework;
using OpenSim.Framework.Console;
using OpenSim.Region.Framework.Interfaces;
using OpenSim.Region.CoreModules.Framework.InterfaceCommander;
using OpenSim.Region.Framework.Scenes.Serialization;
using OpenSim.Region.Framework.Scenes;
using GitSharp;
using GitSharp.Commands;

namespace Careminster.Git
{
    public class Gitminster : IRegionModule, ICommandableModule
    {
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);
        private bool m_Enabled; //Git connector enabled
        private string m_repoPath = "git";
        private IConfig m_Config;
        private Scene m_scene;
        private readonly Commander m_commander = new Commander("git");
        public ICommander CommandInterface
        {
            get { return m_commander; }
        }
        private Repository m_repo;
        private OrderedDictionary m_ToUpdate = new OrderedDictionary();
        private HashSet<string> m_Added = new HashSet<string>();
        private OrderedDictionary m_ToDelete = new OrderedDictionary();
        private int frame = 0;
        private bool m_NeedsCommit = false;
        private int m_changes = 0;
        private int m_commitFrameInterval = 360000;

        public string Name
        {
            get
            {
                return "Gitminster";
            }
        }
        public bool IsSharedModule
        {
            get
            {
                return false;
            }
        }

        public void Initialise(Scene scene, IConfigSource config)
        {
            m_Config = config.Configs["Git"];
            m_scene = scene;
            m_scene.RegisterModuleInterface<IRegionModule>(this);
            m_scene.EventManager.OnPluginConsole += onPluginConsole;
            
            InstallCommands();
            if (m_Config == null)
            {
                m_log.Info("[Git] Gitminster module disabled");
                return;
            }
            else
            {
                m_Enabled = m_Config.GetBoolean("Enabled", false);
                if (!m_Enabled)
                {
                    m_log.Info("[Git] Gitminster module disabled");
                    return;
                }
                Enable(null);
            }

        }
        private void Disable(object o)
        {
            backup(null, true);
            Commit("Final commit; Gitminster going offline", true);
            m_Enabled = false;
            m_scene.SceneGraph.OnAttachToBackup -= onAttachToBackup;
            m_scene.SceneGraph.OnDetachFromBackup -= onDetachFromBackup;
            m_scene.SceneGraph.OnChangeBackup -= onChangedBackup;
            m_scene.EventManager.OnFrame -= tick;
            m_scene.EventManager.OnBackup -= backup;
            m_log.Info("[Git] Gitminster disabled.");
        }
        private void Enable(object o)
        {
              

            m_repoPath = m_Config.GetString("RepoPath", "git");
            m_commitFrameInterval = m_Config.GetInt("CommitFrameInterval", 360000);
            if (!(m_repoPath.Substring(m_repoPath.Length - 1) == "/" || m_repoPath.Substring(m_repoPath.Length - 1) == "\\"))
            {
                m_repoPath += "/";
            }
            m_repoPath += m_scene.RegionInfo.RegionID.ToString()+"/";

            if (!Directory.Exists(m_repoPath))
            {
                m_log.Debug("[Git] Creating path "+m_repoPath);
                try
                {
                    Directory.CreateDirectory(m_repoPath);
                }
                catch
                {
                    m_log.Error("[Git] Couldn't create repo directory. Module disabled.");
                    m_Enabled = false;
                    return;
                }
            }
                
            

            m_Enabled = true;

            

            m_repo = new Repository(m_repoPath);
            if (Repository.IsValid(m_repoPath, false))
            {
                //Use existing repo
                m_log.Debug("[Git] Found existing repository for region " + m_scene.RegionInfo.RegionName);
                m_repo = new Repository(m_repoPath);
            }
            else
            {
                //Initialise a new repo
                m_log.Debug("[Git] Creating a new repository for region " + m_scene.RegionInfo.RegionName);
                Repository.Init(m_repoPath, false);
                m_repo = new Repository(m_repoPath);
            }

            if (m_repo.Status.Added.Count > 0 || m_repo.Status.Removed.Count > 0)
            {
                Commit commit = m_repo.Commit("Uncommitted local changes - region crash", new Author(m_scene.RegionInfo.RegionName, m_scene.RegionInfo.RegionID.ToString() + "@meta7.com"));
                m_log.Debug("[Git] Committing changes which were queued before the region crashed");
            }

            

            m_scene.SceneGraph.OnAttachToBackup += onAttachToBackup;
            m_scene.SceneGraph.OnDetachFromBackup += onDetachFromBackup;
            m_scene.SceneGraph.OnChangeBackup += onChangedBackup;
            m_scene.EventManager.OnFrame += tick;
            m_scene.EventManager.OnBackup += backup;

            m_log.Info("[Git] Gitminster online.");
        }
        private void HandleCommit(Object[] args)
        {
            if (!m_Enabled)
            {
                m_log.Error("[Git] Gitminster is not enabled");
                return;
            }
            backup(null, true);
            Commit("Requested by console", true);
        }
        private void DoClear(object o)
        {
            //Commit any uncommitted committy committs.
            backup(null, true);
            Commit("Preparing for clear", true);
            

            //Now, delete all scene objects.
            m_log.Info("[Git] Clearing the scene..");
            m_scene.DeleteAllSceneObjects();

            //And delete all files from our git repo
            Directory.SetCurrentDirectory(m_repoPath);
            string[] fileEntries = Directory.GetFiles("objects/");
             lock (m_repo)
            {                
                foreach (string fileName in fileEntries)
                {
                    try
                    {
                        m_repo.Index.Delete(fileName);
                        m_changes++;
                        m_NeedsCommit = true;
                    }
                    catch
                    {
                        //Do nothing
                    }
                }
            }

            //Now commit
            Commit("Clear command issued from the console", true);
            m_log.Info("[Git] All done.");

            m_scene.SceneGraph.OnAttachToBackup += onAttachToBackup;
            m_scene.SceneGraph.OnDetachFromBackup += onDetachFromBackup;
            m_scene.SceneGraph.OnChangeBackup += onChangedBackup;
            m_scene.EventManager.OnFrame += tick;
            m_scene.EventManager.OnBackup += backup;
        }
        private void DoRestore(object o)
        {
            try
            {
                bool safe = (bool)o;

                //Now, delete all scene objects.
                m_log.Info("[Git] Clearing the scene..");
                m_scene.DeleteAllSceneObjects();

                //Yay.
                m_log.Info("[Git] Beginning object restore..");
                string[] fileEntries = Directory.GetFiles(m_repoPath + "objects/");
                int files = 0;
                foreach (string fileName in fileEntries)
                {
                    try
                    {
                        lock (m_repo) //Locking this here because we don't want to be doing this at the same time as something else.
                        {
                            files++;
                            if ((files % 200) == 0)
                            {
                                m_log.Info("[Git] Restored " + files.ToString() + " objects");
                            }
                            StreamReader streamReader = new StreamReader(fileName);
                            string data = streamReader.ReadToEnd();
                            data = data.Substring(39);
                            streamReader.Close();
                            SceneObjectGroup sog = SceneXmlLoader.DeserializeGroupFromXml2(data);
                            if (!safe || ((sog.GetEffectivePermissions() & (uint)PermissionMask.Copy) != 0)) // PERM_COPY
                            {
                                m_scene.AddSceneObject(sog);
                                sog.HasGroupChanged = true;
                                sog.SendGroupFullUpdate();
                            }
                            else
                            {
                                m_log.Info("[Git] Skipped '" + sog.Name + "' - No Copy");
                            }
                        }
                    }
                    catch(Exception e)
                    {
                        m_log.Error("[Git] Error restoring group, "+e.Message);
                    }
                }
                m_log.Info("[Git] Restored " + files.ToString() + " objects. All done!");
            }
            finally
            {
                //Gimmeh mai events back
                m_scene.SceneGraph.OnAttachToBackup += onAttachToBackup;
                m_scene.SceneGraph.OnDetachFromBackup += onDetachFromBackup;
                m_scene.SceneGraph.OnChangeBackup += onChangedBackup;
                m_scene.EventManager.OnFrame += tick;
                m_scene.EventManager.OnBackup += backup;
            }
        }
        private void HandleClear(Object[] args)
        {
            if (!m_Enabled)
            {
                m_log.Error("[Git] Gitminster is not enabled");
                return;
            }
            //First, unhook from all events.
            m_scene.SceneGraph.OnAttachToBackup -= onAttachToBackup;
            m_scene.SceneGraph.OnDetachFromBackup -= onDetachFromBackup;
            m_scene.SceneGraph.OnChangeBackup -= onChangedBackup;
            m_scene.EventManager.OnFrame -= tick;
            m_scene.EventManager.OnBackup -= backup;

            Util.FireAndForget(DoClear, false);

        }
        private void HandleRestore(Object[] args)
        {   if (!m_Enabled)
            {
                m_log.Error("[Git] Gitminster is not enabled");
                return;
            }

            //First, unhook from all events.
            m_scene.SceneGraph.OnAttachToBackup -= onAttachToBackup;
            m_scene.SceneGraph.OnDetachFromBackup -= onDetachFromBackup;
            m_scene.SceneGraph.OnChangeBackup -= onChangedBackup;
            m_scene.EventManager.OnFrame -= tick;
            m_scene.EventManager.OnBackup -= backup;

            //Commit any uncommitted committy committs.
            backup(null, true);
            Commit("Preparing for restore", true);

            if (args.Length > 0)
            {
                //Roll back to commit specified
                m_log.Info("[Git] Rolling back to commit " + (string)args[0]);
                m_repo.CurrentBranch.Reset((string)args[0],ResetBehavior.Hard);
            }

            Util.FireAndForget(DoRestore, false);
        }
        private void HandleCheckoutSafe(Object[] args)
        {
            if (!m_Enabled)
            {
                m_log.Error("[Git] Gitminster is not enabled");
                return;
            }
            string branchname = (string)args[0];


            //Now, unhook from all events.
            m_scene.SceneGraph.OnAttachToBackup -= onAttachToBackup;
            m_scene.SceneGraph.OnDetachFromBackup -= onDetachFromBackup;
            m_scene.SceneGraph.OnChangeBackup -= onChangedBackup;
            m_scene.EventManager.OnFrame -= tick;
            m_scene.EventManager.OnBackup -= backup;

            //Commit any uncommitted committy committs.
            backup(null, true);
            Commit("Preparing for checkout", true);

            m_log.Info("[Git] Now checking out branch " + branchname + "..");
            try
            {
                lock (m_repo)
                {
                    Branch b = new Branch(m_repo, branchname);
                    b.Checkout();
                    Util.FireAndForget(DoRestore, true);
                }
            }
            catch
            {
                m_scene.SceneGraph.OnAttachToBackup += onAttachToBackup;
                m_scene.SceneGraph.OnDetachFromBackup += onDetachFromBackup;
                m_scene.SceneGraph.OnChangeBackup += onChangedBackup;
                m_scene.EventManager.OnFrame += tick;
                m_scene.EventManager.OnBackup += backup;
            }

            
        }
        private void HandleCheckout(Object[] args)
        {
            if (!m_Enabled)
            {
                m_log.Error("[Git] Gitminster is not enabled");
                return;
            }
            string branchname = (string)args[0];

            //Now, unhook from all events.
            m_scene.SceneGraph.OnAttachToBackup -= onAttachToBackup;
            m_scene.SceneGraph.OnDetachFromBackup -= onDetachFromBackup;
            m_scene.SceneGraph.OnChangeBackup -= onChangedBackup;
            m_scene.EventManager.OnFrame -= tick;
            m_scene.EventManager.OnBackup -= backup;

            //Commit any uncommitted committy committs.
            backup(null, true);
            Commit("Preparing for checkout", true);           

            m_log.Info("[Git] Now checking out branch " + branchname + "..");
            try
            {
                lock (m_repo)
                {
                    Branch b = new Branch(m_repo, branchname);
                    b.Checkout();
                    Util.FireAndForget(DoRestore, false);
                }
            }
            catch
            {
                m_scene.SceneGraph.OnAttachToBackup += onAttachToBackup;
                m_scene.SceneGraph.OnDetachFromBackup += onDetachFromBackup;
                m_scene.SceneGraph.OnChangeBackup += onChangedBackup;
                m_scene.EventManager.OnFrame += tick;
                m_scene.EventManager.OnBackup += backup;
            }

        }
        private void HandleBranchDelete(Object[] args)
        {
            if (!m_Enabled)
            {
                m_log.Error("[Git] Gitminster is not enabled");
                return;
            }
            string branchname = (string)args[0];

            //Commit any uncommitted committy committs.
            backup(null, true);
            Commit("Preparing for branch delete", true);

            //Perform branch delete
            try
            {
                lock (m_repo)
                {

                    Branch d = new Branch(m_repo, branchname);
                    d.Delete();
                    m_log.Info("[Git] Branch " + branchname + " deleted.");
                }
            }
            catch(Exception e)
            {
                m_log.Error("[Git] Couldn't Delete the branch. "+e.Message);
            }
        }
        private void HandleBranch(Object[] args)
        {
            if (!m_Enabled)
            {
                m_log.Error("[Git] Gitminster is not enabled");
                return;
            }
            string branchname = (string)args[0];

            //Commit any uncommitted committy committs.
            backup(null, true);
            Commit("Preparing for branch", true);

            //Create branch.
            try
            {
                lock (m_repo)
                {
                    Branch b = GitSharp.Branch.Create(m_repo, branchname);
                    m_log.Info("[Git] Branch " + branchname + " created.");
                }
            }
            catch
            {
                m_log.Error("[Git] Couldn't create the branch.");
            }
            
        }
        private void HandleReload(Object[] args)
        {
            if (!m_Enabled)
            {
                m_log.Error("[Git] Gitminster is not enabled");
                return;
            }
            //Now, unhook from all events.
            m_scene.SceneGraph.OnAttachToBackup -= onAttachToBackup;
            m_scene.SceneGraph.OnDetachFromBackup -= onDetachFromBackup;
            m_scene.SceneGraph.OnChangeBackup -= onChangedBackup;
            m_scene.EventManager.OnFrame -= tick;
            m_scene.EventManager.OnBackup -= backup;

            //Commit any uncommitted committy committs.
            backup(null, true);
            Commit("Preparing for reload",true);

            Util.FireAndForget(DoRestore, false);
        }
        private void HandleRestoreSafe(Object[] args)
        {

            if (!m_Enabled)
            {
                m_log.Error("[Git] Gitminster is not enabled");
                return;
            }

            //First, unhook from all events.
            m_scene.SceneGraph.OnAttachToBackup -= onAttachToBackup;
            m_scene.SceneGraph.OnDetachFromBackup -= onDetachFromBackup;
            m_scene.SceneGraph.OnChangeBackup -= onChangedBackup;
            m_scene.EventManager.OnFrame -= tick;
            m_scene.EventManager.OnBackup -= backup;

            //Commit any uncommitted committy committs.
            backup(null, true);
            Commit("Preparing for restore", true);

            if (args.Length > 0)
            {
                //Roll back to commit specified
                m_log.Debug("[Git] Rolling back to commit " + (string)args[0]);
                m_repo.CurrentBranch.Reset((string)args[0], ResetBehavior.Hard);
            }

            Util.FireAndForget(DoRestore, true);
        }
        private void InstallCommands()
        {
            Command gitcommit = new Command("commit", CommandIntentions.COMMAND_NON_HAZARDOUS, HandleCommit, "Commit all unsaved changes to th git repo");
            Command gitrestore = new Command("restore", CommandIntentions.COMMAND_HAZARDOUS, HandleRestore, "Restore a specific commit from the git repo to the current scene");
            Command gitrestoresafe = new Command("restoresafe", CommandIntentions.COMMAND_HAZARDOUS, HandleRestoreSafe, "Restore a specific commit from the git repo to the current scene, skipping NoCopy objects");
            Command gitreload = new Command("reload", CommandIntentions.COMMAND_HAZARDOUS, HandleReload, "Reloads the current scene from the git repo");
            Command gitclear = new Command("clear", CommandIntentions.COMMAND_HAZARDOUS, HandleClear, "Wipes all objects from the scene and commits.");
            Command gitbranch = new Command("branch", CommandIntentions.COMMAND_HAZARDOUS, HandleBranch, "Creates a new branch bases on the current one");
            Command gitcheckout = new Command("checkout", CommandIntentions.COMMAND_HAZARDOUS, HandleCheckout, "Switches to another branch.");
            Command gitcheckoutsafe = new Command("checkoutsafe", CommandIntentions.COMMAND_HAZARDOUS, HandleCheckoutSafe, "Switches to another branch, but skips NoCopy objects.");
            Command gitdeletebranch = new Command("deletebranch", CommandIntentions.COMMAND_HAZARDOUS, HandleBranchDelete, "Deletes a branch which is not currently active");
            Command gitenable = new Command("enable", CommandIntentions.COMMAND_HAZARDOUS, Enable, "Enables the Gitminster plugin");
            Command gitdisable = new Command("disable", CommandIntentions.COMMAND_HAZARDOUS, Disable, "Disables the Gitminster plugin");
            
            
            gitrestore.AddArgument("hash", "The commit hash you wish to restore", "String");
            gitrestoresafe.AddArgument("hash", "The commit hash you wish to restore", "String");
            gitbranch.AddArgument("branchname", "The name of the branch you wish to create", "String");
            gitcheckout.AddArgument("branchname", "The name of the branch you wish to switch to", "String");
            gitcheckoutsafe.AddArgument("branchname", "The name of the branch you wish to switch to", "String");
            gitdeletebranch.AddArgument("branchname", "The name of the branch you wish to delete", "String");


            m_commander.RegisterCommand("commit", gitcommit);
            m_commander.RegisterCommand("restore", gitrestore);
            m_commander.RegisterCommand("restoresafe", gitrestoresafe);
            m_commander.RegisterCommand("reload", gitreload);
            m_commander.RegisterCommand("clear", gitclear);
            m_commander.RegisterCommand("branch", gitbranch);
            m_commander.RegisterCommand("checkout", gitcheckout);
            m_commander.RegisterCommand("checkoutsafe", gitcheckoutsafe);
            m_commander.RegisterCommand("deletebranch", gitdeletebranch);
            m_commander.RegisterCommand("enable", gitenable);
            m_commander.RegisterCommand("disable", gitdisable);

            m_scene.RegisterModuleCommander(m_commander);

        }
        private void DoCommitAsync(object o)
        {
            try
            {
                if (m_changes == 0)
                {
                    m_log.Debug("[Git] Skipping commit, no changes?");
                    return;
                }
                string message = (string)o;
                m_log.Debug("[Git] Scanning " + m_changes.ToString() + " files for changes..");
                m_changes = 0;
                m_Added.Clear();
                m_NeedsCommit = false;
                lock (m_repo)
                {
                    Commit commit = m_repo.Commit(message, new Author(m_scene.RegionInfo.RegionName, m_scene.RegionInfo.RegionID.ToString() + "@meta7.com"));
                }
                m_log.Debug("[Git] Done");
            }
            finally
            {
                m_scene.EventManager.OnFrame += tick;
                m_scene.EventManager.OnBackup += backup;
            }
        }
        private void Commit(string message, bool synchronous)
        {
            //Committing can take some time, so spawn off to a seperate thread,
            //and remove our events so we don't have a lock situation.
            m_scene.EventManager.OnFrame -= tick;
            m_scene.EventManager.OnBackup -= backup;
            if (!synchronous)
            {
                Util.FireAndForget(DoCommitAsync, message);
            }
            else
            {
                DoCommitAsync((object)message);
            }
            
        }
        private void backup(IRegionDataStore datastore, bool m_forced)
        {
            if (m_forced)
            {
                try
                {
                    m_scene.EventManager.OnFrame -= tick;
                    m_scene.EventManager.OnBackup -= backup;

                    m_log.Debug("[Git] Adding all " + m_ToUpdate.Count.ToString() + " remaining objects..");
                    lock (m_repo)
                    {
                        int n = m_ToUpdate.Count;
                        for (int x = 0; x < n; x++)
                        {
                            SceneObjectGroup candidate = (SceneObjectGroup)m_ToUpdate[m_ToUpdate.Count - 1];
                            m_ToUpdate.RemoveAt(m_ToUpdate.Count - 1);
                            m_changes++;
                            m_NeedsCommit = true;
                            AddGroup(candidate);
                        }
                        //m_ToUpdate.Clear();
                    }
                    m_log.Debug("[Git] Done");
                }
                catch
                {
                    m_log.Error("[Git] Failed to backup all queued objects");
                }
                finally
                {
                    m_scene.EventManager.OnFrame += tick;
                    m_scene.EventManager.OnBackup += backup;
                }
            }
        }
        private void tick()
        {
            frame++;
            int speed = 50;
            int dspeed = 50;
            //If we've got a large queue, clear it quickly, otherwise, don't lag up our heartbeat thread too much!
            if (m_ToUpdate.Count > 10) speed = 2;
            if (m_ToDelete.Count > 10) dspeed = 2; 

            if ((frame % speed) == 0)
            {
                //Add one object per tick
                if (m_ToUpdate.Count > 0)
                {
                    SceneObjectGroup candidate = (SceneObjectGroup)m_ToUpdate[0];
                    m_ToUpdate.RemoveAt(0);
                    m_NeedsCommit = true;
                    m_changes++;
                    AddGroup(candidate);
                }
            }

            if ((frame % dspeed) == 0)
            {
                //Delete one object per tick (Deleting is a lot less intensive)
                if (m_ToDelete.Count > 0)
                {
                    SceneObjectGroup candidate = (SceneObjectGroup)m_ToDelete[0];
                    m_ToDelete.RemoveAt(0);
                    m_NeedsCommit = true;
                    m_changes++;
                    DeleteGroup(candidate);
                }
            }

            if (frame > m_commitFrameInterval)
            {
                if (m_NeedsCommit)
                {
                    Commit("Changes so far",false);
                }
                frame = 0;
            }
        }
        private void AddGroup(SceneObjectGroup sog)
        {
            string primsdir = m_repoPath + "objects";
            try
            {
                if (!Directory.Exists(primsdir))
                {
                    try
                    {
                        Directory.CreateDirectory(primsdir);
                    }
                    catch
                    {
                        m_log.Error("[Git] Couldn't create directory: " + primsdir);
                        return;
                    }
                }
                string primfile = "objects/"+ sog.UUID.ToString();
                //XmlTextWriter tw = new XmlTextWriter(m_repoPath + primfile,System.Text.Encoding.UTF8);
                XElement code = XElement.Parse(sog.ToXml2());
                foreach (XElement e in code.Descendants("LocalId"))
                {
                    e.Value = "0";
                }
                foreach (XElement e in code.Descendants("ParentID"))
                {
                    e.Value = "0";
                }
                code.Save(m_repoPath + primfile);
                m_Added.Add(sog.UUID.ToString());
                lock (m_repo)
                {
                    m_repo.Index.Add(primfile);
                }
            }
            catch(Exception e)
            { 
                m_log.Error("[Git] Exception while adding object: "+e.Message);
            }
        }

        private void DeleteGroup(SceneObjectGroup sog)
        {
            try
            {
                if (m_Added.Contains(sog.UUID.ToString()))
                {
                    //This sog has been added but not committed, so, commit now
                    Commit("Persisting object " + sog.UUID.ToString(), true);
                }
                
                lock (m_repo)
                {
                    m_repo.Index.Delete("objects/" + sog.UUID.ToString());
                }

                
                m_NeedsCommit = true;
                m_changes++;
            }
            catch
            {
                //This happens a lot with our delayed persistance stuff, so, don't spam
                //m_log.Error("[Git] Failed to delete group "+sog.UUID.ToString());
                return;
            }
        }

        private void Queue(SceneObjectGroup sog)
        {
            if (sog == null) return;
            
            if (m_ToDelete.Contains(sog.UUID.ToString()))
            {
                m_ToDelete.Remove(sog.UUID.ToString());
            }

            if (!m_ToUpdate.Contains(sog.UUID.ToString()))
            {
                m_ToUpdate.Insert(m_ToUpdate.Count, sog.UUID.ToString(), sog);
            }
            else
            {
                //Bump to the back of the queue
                m_ToUpdate.Remove(sog.UUID.ToString());
                m_ToUpdate.Insert(m_ToUpdate.Count,sog.UUID.ToString(), sog);
            }
        }

        private void onAttachToBackup(SceneObjectGroup sog)
        {
            if (sog == null) return;
            try
            {
                if (sog.IsAttachment) return;
                Queue(sog);
            }
            catch
            {
                return;
            }
        }

        private void onDetachFromBackup(SceneObjectGroup sog)
        {
            if (sog == null) return;
            try
            {
                if (sog.IsAttachment) return;
                if (m_ToUpdate.Contains(sog.UUID.ToString()))
                {
                    m_ToUpdate.Remove(sog.UUID.ToString());
                }
                if (!m_ToDelete.Contains(sog.UUID.ToString()))
                {
                    m_ToDelete.Insert(m_ToDelete.Count, sog.UUID.ToString(), sog);
                }
            }
            catch
            {
                return;
            }
        }

        private void onPluginConsole(string[] args)
        {
            if (args[0] == "git")
            {
                string[] tmpArgs = new string[args.Length - 2];
                int i;
                for (i = 2; i < args.Length; i++)
                {
                    tmpArgs[i - 2] = args[i];
                }

                m_commander.ProcessConsoleCommand(args[1], tmpArgs);
            }
        }
        private void onChangedBackup(SceneObjectGroup sog)
        {
            if (sog == null) return;
            if (sog.IsAttachment) return;
            try
            {
                Queue(sog);
            }
            catch
            {
                return;
            }
        }

        public void PostInitialise()
        {
            if (!m_Enabled) return;
            try
            {
                //Add RegionOnline as a commit. This will also commit any changes that occurred before
                //the region crashed.. which is nice.
                StreamWriter tw = new StreamWriter(m_repoPath + "RegionOnline.txt");
                tw.WriteLine("Region " + m_scene.RegionInfo.RegionName + " Online: " + DateTime.Now.ToString());
                tw.Close();
                m_repo.Index.Add("RegionOnline.txt");
                Commit commit = m_repo.Commit("Region online at " + DateTime.Now.ToString(), new Author(m_scene.RegionInfo.RegionName, m_scene.RegionInfo.RegionID.ToString() + "@meta7.com"));
                m_log.Debug("[Git] Committing startup time");
            }
            catch(Exception e)
            {
                m_log.Error("[Git] Failed to make RegionOnline commit: " + e.Message);
            }
        }

        public void Close()
        {
            if (!m_Enabled) return;
            backup(null, true);
            Commit("Final commit; region shutdown", true);
        }
    }
}
