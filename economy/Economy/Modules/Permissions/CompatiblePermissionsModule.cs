// ******************************************************************
// Copyright (c) 2008, 2009 Melanie Thielker
//
// All rights reserved
//

using OpenMetaverse;
using Nini.Config;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using log4net;
using OpenSim.Framework;
using OpenSim.Region.Framework.Interfaces;
using OpenSim.Region.Framework.Scenes;
using OpenSim.Services.Interfaces;
using Mono.Addins;

namespace Careminster.Modules.Permissions
{
    [Extension(Path = "/OpenSim/RegionModules", NodeName = "RegionModule")]
    public class CompatiblePermissionsModule : INonSharedRegionModule
    {
        protected Scene m_Scene;
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        #region Constants
        
        private uint PERM_ALL = (uint)2147483647;
        private uint PERM_COPY = (uint)32768;
        private uint PERM_MODIFY = (uint)16384;
//        private uint PERM_MOVE = (uint)524288;
        private uint PERM_TRANS = (uint)8192;
        private uint PERM_LOCKED = (uint)540672;

        private bool m_AllowGridGods = true;
        private bool m_EstateOwnerIsGod = false;
        private bool m_EstateManagerIsGod = false;
        private bool m_Enabled = false;
        private InventoryFolderImpl m_LibraryRootFolder;

        private IFriendsModule m_friendsModule = null;

        protected InventoryFolderImpl LibraryRootFolder
        {
            get
            {
                if (m_LibraryRootFolder != null)
                    return m_LibraryRootFolder;

                ILibraryService lib = m_Scene.RequestModuleInterface<ILibraryService>();
                if (lib != null)
                {
                    m_LibraryRootFolder = lib.LibraryRootFolder;
                }
                return m_LibraryRootFolder;
            }
        }

        #endregion

        #region IRegionModule Members

        public void Initialise(IConfigSource config)
        {
            IConfig myConfig = config.Configs["Startup"];
            
            string permissionModules = myConfig.GetString("permissionmodules", "DefaultPermissionsModule");

            List<string> modules=new List<string>(permissionModules.Split(','));

            if(!modules.Contains("CompatiblePermissionsModule"))
                return;

            m_AllowGridGods = myConfig.GetBoolean("allow_grid_gods", true);
            m_EstateOwnerIsGod = myConfig.GetBoolean("estate_owner_is_god", false);
            m_EstateManagerIsGod = myConfig.GetBoolean("estate_manager_is_god", false);

            m_Enabled = true;
        }

        public void AddRegion(Scene scene)
        {
            if (!m_Enabled)
                return;

            m_Scene = scene;
            
            //Register functions with Scene External Checks!
            m_Scene.Permissions.OnBypassPermissions += BypassPermissions;
            m_Scene.Permissions.OnPropagatePermissions += PropagatePermissions;
            m_Scene.Permissions.OnAbandonParcel += CanAbandonParcel;
            m_Scene.Permissions.OnReclaimParcel += CanReclaimParcel;
            m_Scene.Permissions.OnDeedParcel += CanDeedParcel;
            m_Scene.Permissions.OnIsGod += IsGod;
            m_Scene.Permissions.OnDuplicateObject += CanDuplicateObject;
            m_Scene.Permissions.OnDeleteObject += CanDeleteObject;
            m_Scene.Permissions.OnEditObject += CanEditObject;
            m_Scene.Permissions.OnEditObjectInventory += CanEditObjectInventory;
            m_Scene.Permissions.OnEditParcel += CanEditParcel;
            m_Scene.Permissions.OnEditScript += CanEditScript;
            m_Scene.Permissions.OnEditNotecard += CanEditNotecard;
            m_Scene.Permissions.OnInstantMessage += CanInstantMessage;
            m_Scene.Permissions.OnInventoryTransfer += CanInventoryTransfer;
            m_Scene.Permissions.OnIssueEstateCommand += CanIssueEstateCommand;
            m_Scene.Permissions.OnGenerateClientFlags += GenerateClientFlags;
            m_Scene.Permissions.OnMoveObject += CanMoveObject;
            m_Scene.Permissions.OnObjectEntry += CanObjectEntry;
            m_Scene.Permissions.OnReturnObjects += CanReturnObjects;
            m_Scene.Permissions.OnRezObject += CanRezObject;
            m_Scene.Permissions.OnRunConsoleCommand += CanRunConsoleCommand;
            m_Scene.Permissions.OnRunScript += CanRunScript;
            m_Scene.Permissions.OnSellParcel += CanSellParcel;
            m_Scene.Permissions.OnTakeObject += CanTakeObject;
            m_Scene.Permissions.OnTakeCopyObject += CanTakeCopyObject;
            m_Scene.Permissions.OnTerraformLand += CanTerraformLand;
            m_Scene.Permissions.OnViewScript += CanViewScript;
            m_Scene.Permissions.OnViewNotecard += CanViewNotecard;
            m_Scene.Permissions.OnLinkObject += CanLinkObject;
            m_Scene.Permissions.OnDelinkObject += CanDelinkObject;
            m_Scene.Permissions.OnBuyLand += CanBuyLand;
            m_Scene.Permissions.OnCopyObjectInventory += CanCopyObjectInventory;
            m_Scene.Permissions.OnDeleteObjectInventory += CanDeleteObjectInventory;
            m_Scene.Permissions.OnCreateObjectInventory += CanCreateObjectInventory;
            m_Scene.Permissions.OnCreateUserInventory += CanCreateUserInventory;
            m_Scene.Permissions.OnCopyUserInventory += CanCopyUserInventory;
            m_Scene.Permissions.OnEditUserInventory += CanEditUserInventory;
            m_Scene.Permissions.OnDeleteUserInventory += CanDeleteUserInventory;
            m_Scene.Permissions.OnTeleport += CanTeleport;
            m_Scene.Permissions.OnResetScript += CanResetScript;
        }

        public void RegionLoaded(Scene scene)
        {
            if (!m_Enabled)
                return;

            m_friendsModule = m_Scene.RequestModuleInterface<IFriendsModule>();

            if (m_friendsModule == null)
                m_log.Error("[PERMISSIONS]: Friends module not found, friend permissions will not work");
            else
                m_log.Info("[PERMISSIONS]: Friends module found, friend permissions enabled");
        }

        public void RemoveRegion(Scene scene)
        {
        }

        public void Close()
        {
        }

        public Type ReplacableInterface
        {
            get { return null; } // Must be manually configured for now
        }

        public string Name
        {
            get { return "CompatiblePermissionsModule"; }
        }

        public Type ReplaceableInterface
        {
            get { return null; }
        }

        #endregion        

        #region Helper Functions
        protected void SendPermissionError(UUID user, string reason)
        {
            m_Scene.EventManager.TriggerPermissionError(user, reason);
        }

        protected bool IsAdministrator(UUID user)
        {
            if (user == UUID.Zero)
                return false;

            if (m_EstateOwnerIsGod && IsEstateOwner(user))
                return true;
            if (m_EstateManagerIsGod && IsEstateManager(user))
                return true;

            ScenePresence sp = m_Scene.GetScenePresence(user);
            if (sp != null)
            {
                if (sp.UserLevel >= 200)
                    return true;

                return false;
            }

            UserAccount account = m_Scene.UserAccountService.GetUserAccount(m_Scene.RegionInfo.ScopeID, user);
            if (account != null)
            {
                if (account.UserLevel >= 200 && m_AllowGridGods)
                    return true;
            }

            return false;
        }

        protected bool IsEstateOwner(UUID user)
        {
            if(IsAdministrator(user))
                return true;

            // If there is no estate owner, return false
            if (m_Scene.RegionInfo.EstateSettings.EstateOwner != UUID.Zero)
            {
                if (m_Scene.RegionInfo.EstateSettings.EstateOwner == user)
                    return true;
            }

            return false;
        }

        protected bool IsEstateManager(UUID user)
        {
            if(IsEstateOwner(user))
                return true;

            return m_Scene.RegionInfo.EstateSettings.IsEstateManager(user);
        }

        protected bool IsFriendWithPerms(UUID user,UUID objectOwner)
        {

            if (user == UUID.Zero)
                return false;

            if (m_friendsModule == null)
                return false;

            uint friendPerms = m_friendsModule.GetFriendPerms(user, objectOwner);
            if ((friendPerms & (uint)FriendRights.CanModifyObjects) != 0)
                return true;

            return false;
        }

        protected bool CheckGroupPowers(UUID agentID, UUID groupID, ulong mask)
        {
            ScenePresence sp = m_Scene.GetScenePresence(agentID);
            if (sp == null)
                return false;

            ulong powers = sp.ControllingClient.GetGroupPowers(groupID);
            if (powers == 0)
                return false;

            if ((powers & mask) == mask)
                return true;

            return false;
        }

#endregion

        #region Object Permissions

        public uint GenerateClientFlags(UUID user, UUID objID)
        {
            SceneObjectPart task=m_Scene.GetSceneObjectPart(objID);
            ScenePresence presence=m_Scene.GetScenePresence(user);
            
            if (task == null || presence == null)
                return (uint)0;

            uint objflags = task.GetEffectiveObjectFlags();
            UUID objectOwner = task.OwnerID;


            objflags &= (uint)
                ~(PrimFlags.ObjectCopy |
                  PrimFlags.ObjectModify |
                  PrimFlags.ObjectMove |
                  PrimFlags.ObjectTransfer |
                  PrimFlags.ObjectYouOwner |
                  PrimFlags.ObjectAnyOwner |
                  PrimFlags.ObjectOwnerModify |
                  PrimFlags.ObjectYouOfficer);

            // Object owners should be able to edit their own content
            if (user == objectOwner || IsFriendWithPerms(user, objectOwner))
            {
                uint flags=ApplyObjectModifyMasks(task.OwnerMask, objflags) |
                        (uint)PrimFlags.ObjectYouOwner |
                        (uint)PrimFlags.ObjectAnyOwner;
                if((task.OwnerMask & (uint)PermissionMask.Modify) != 0)
                    flags |= (uint)PrimFlags.ObjectOwnerModify;

                return flags;
            }

            if (IsAdministrator(user)) // && presence.GodLevel >= 250f)
            {
                return ApplyObjectModifyMasks(task.OwnerMask, objflags) |
                        (uint)PrimFlags.ObjectYouOwner |
                        (uint)PrimFlags.ObjectAnyOwner |
                        (uint)PrimFlags.ObjectOwnerModify;
            }

            if (task.OwnerID == task.GroupID && CheckGroupPowers(user, task.GroupID, (ulong)GroupPowers.ObjectManipulate))
            {
                uint flags=ApplyObjectModifyMasks(task.OwnerMask, objflags) |
                        (uint)PrimFlags.ObjectYouOwner |
                        (uint)PrimFlags.ObjectGroupOwned |
                        (uint)PrimFlags.ObjectAnyOwner;
                if((task.OwnerMask & (uint)PermissionMask.Modify) != 0)
                    flags |= (uint)PrimFlags.ObjectOwnerModify;

                return flags;
            }
            else if (CheckGroupPowers(user, task.GroupID, (ulong)GroupPowers.ObjectManipulate))
            {
                uint flags=ApplyObjectModifyMasks(task.GroupMask, objflags) |
                        (uint)PrimFlags.ObjectMove |
                        (uint)PrimFlags.ObjectAnyOwner;

                return flags;
            }

            if ((task.OwnerID == task.GroupID) && presence.ControllingClient.IsGroupMember(task.GroupID))
                objflags |= (uint)PrimFlags.ObjectGroupOwned;
            else if (task.OwnerID != UUID.Zero)
                objflags |= (uint)PrimFlags.ObjectAnyOwner;

            return ApplyObjectModifyMasks(task.EveryoneMask, objflags);
        }

        private uint ApplyObjectModifyMasks(uint setPermissionMask, uint objectFlagsMask)
        {
            if ((setPermissionMask & (uint)PermissionMask.Copy) != 0)
            {
                objectFlagsMask |= (uint)PrimFlags.ObjectCopy;
            }

            if ((setPermissionMask & (uint)PermissionMask.Move) != 0)
            {
                objectFlagsMask |= (uint)PrimFlags.ObjectMove;
            }

            if ((setPermissionMask & (uint)PermissionMask.Modify) != 0)
            {
                objectFlagsMask |= (uint)PrimFlags.ObjectModify;
            }

            if ((setPermissionMask & (uint)PermissionMask.Transfer) != 0)
            {
                objectFlagsMask |= (uint)PrimFlags.ObjectTransfer;
            }

            return objectFlagsMask;
        }

        protected bool GenericObjectPermission(UUID currentUser, UUID objId, bool denyOnLocked)
        {
            if (IsAdministrator(currentUser))
                return true;

            // Default: deny
            bool permission = false;
            bool locked = false;

            SceneObjectPart obj = m_Scene.GetSceneObjectPart(objId);
            if(obj == null)
                return false;
            SceneObjectGroup group = obj.ParentGroup;
            if(group == null)
                return false;

            ScenePresence presence=m_Scene.GetScenePresence(currentUser);

            if(presence == null)
                return false;

            UUID objectOwner = group.OwnerID;
            locked = ((group.RootPart.OwnerMask & PERM_LOCKED) == 0);

            if (locked && denyOnLocked) //&& presence.GodLevel >= 250f))
            {
                return false;
            }

            // Object owners should be able to edit their own content
            if (currentUser == objectOwner)
            {
                permission = true;
            }
            else if(obj.IsAttachment)
            {
                permission = false;
            }
            else if(IsFriendWithPerms(currentUser, objectOwner))
            {
                permission = true;
            }
            else if (CheckGroupPowers(currentUser, obj.GroupID,
                    (ulong)GroupPowers.ObjectManipulate))
            {
                if (obj.OwnerID == obj.GroupID)
                    permission = true;
                else
                {
                    if (obj.GroupMask != 0)
                        permission = true;
                }
            }

            return permission;
        }


        #endregion

        #region Generic Permissions
        protected bool GenericCommunicationPermission(UUID user, UUID target)
        {
            return true;
        }

        protected bool GenericParcelPermission(UUID user, ILandObject parcel, ulong powers)
        {
            bool permission = false;

            ScenePresence presence=m_Scene.GetScenePresence(user);

            if(presence == null)
                return false;

            if (parcel.LandData.OwnerID == user)
            {
                permission = true;
            }

            if (parcel.LandData.IsGroupOwned)
            {
                if (powers != 0 &&
                        CheckGroupPowers(user, parcel.LandData.GroupID, powers))
                    permission = true;
            }

            if (IsEstateManager(user))
            {
                permission = true;
            }

            if (IsAdministrator(user)) // && presence.GodLevel >= 250f)
            {
                permission = true;
            }

            return permission;
        }

        protected bool GenericParcelPermission(UUID user, Vector3 pos, ulong power)
        {
            ILandObject parcel = m_Scene.LandChannel.GetLandObject(pos.X, pos.Y);
            if (parcel == null) return false;
            return GenericParcelPermission(user, parcel, power);
        }
#endregion
        
        #region Permission Checks
            private bool CanAbandonParcel(UUID user, ILandObject parcel, Scene scene)
            {
                if (parcel.LandData.OwnerID == user)
                {
                    return true;
                }

                ScenePresence presence=m_Scene.GetScenePresence(user);

                if(presence == null)
                    return false;

                return IsEstateManager(user); // && presence.GodLevel >= 250f;
            }

            private bool CanReclaimParcel(UUID user, ILandObject parcel, Scene scene)
            {
                if (parcel.LandData.OwnerID == user)
                {
                    return true;
                }

                ScenePresence presence=m_Scene.GetScenePresence(user);

                if(presence == null)
                    return false;

                return IsEstateManager(user); // && presence.GodLevel >= 250f;
            }

            private bool CanDeedParcel(UUID user, ILandObject parcel, Scene scene)
            {
                if (IsAdministrator(user))
                    return true;

                if (parcel.LandData.OwnerID != user)
                    return false;

                ScenePresence sp = scene.GetScenePresence(user);
                if (sp == null) 
                    return false;

                IClientAPI client = sp.ControllingClient;

                if ((client.GetGroupPowers(parcel.LandData.GroupID) &
                        (long)GroupPowers.LandDeed) == 0)
                    return false;

                return true;
            }

            private bool IsGod(UUID user, Scene scene)
            {
                return IsAdministrator(user);
            }

            private uint GetEffectivePermissions(UUID user, UUID objectID)
            {
                SceneObjectPart obj = m_Scene.GetSceneObjectPart(objectID);
                if (obj == null)
                    return 0;
                SceneObjectGroup group = obj.ParentGroup;
                if (group == null)
                    return 0;
                SceneObjectPart rootPart = group.RootPart;
                if (rootPart == null)
                    return 0;

                ScenePresence presence=m_Scene.GetScenePresence(user);
                if (presence == null)
                    return 0;

                if (IsAdministrator(user)) // && presence.GodLevel >= 250)
                    return PERM_ALL;

                if (rootPart.OwnerID == user)
                    return rootPart.OwnerMask;

                if (rootPart.OwnerID == rootPart.GroupID &&
                        CheckGroupPowers(user, rootPart.GroupID,
                        (ulong)GroupPowers.ObjectManipulate))
                    return rootPart.OwnerMask;

                if (CheckGroupPowers(user, rootPart.GroupID,
                        (ulong)GroupPowers.ObjectManipulate))
                    return rootPart.GroupMask;

                return rootPart.EveryoneMask;
            }

            private bool CanDuplicateObject(int objectCount, UUID objectID, UUID owner, Scene scene, Vector3 objectPosition)
            {
                SceneObjectPart part = scene.GetSceneObjectPart(objectID);
                if (part == null)
                    return false;

                SceneObjectGroup group = part.ParentGroup;
                if (group == null)
                    return false;

                uint perms=GetEffectivePermissions(owner, objectID) & group.GetEffectivePermissions();

                if(!CanRezObject(objectCount, owner, objectPosition, scene))
                    return false;

                if((perms & PERM_COPY) == 0)
                    return false;

                return true;
            }

            private bool CanDeleteObject(UUID objectID, UUID deleter, Scene scene)
            {
                return GenericObjectPermission(deleter, objectID, false);
            }

            private bool CanEditObject(UUID objectID, UUID editorID, Scene scene)
            {
                uint perms=GetEffectivePermissions(editorID, objectID);

                if((perms & PERM_MODIFY) == 0)
                    return false;

                return true;
            }

            private bool CanEditObjectInventory(UUID objectID, UUID editorID, Scene scene)
            {
                SceneObjectPart part = m_Scene.GetSceneObjectPart(objectID);

                if (part.OwnerID != editorID)
                    return false;

                uint perms=GetEffectivePermissions(editorID, objectID);

                if((perms & PERM_MODIFY) == 0)
                    return false;

                return true;
            }

            private bool CanEditParcel(UUID user, ILandObject parcel, Scene scene)
            {
                return GenericParcelPermission(user, parcel, (ulong)GroupPowers.LandDivideJoin);
            }

            private bool CanEditScript(UUID script, UUID objectID, UUID user, Scene scene)
            {
                if (IsAdministrator(user))
                    return true;

                // If you can view it, you can edit it
                // There is no viewing a no mod script
                //
                return CanViewScript(script, objectID, user, scene);
            }

            private bool CanEditNotecard(UUID notecard, UUID objectID, UUID user, Scene scene)
            {
                if (IsAdministrator(user))
                    return true;

                if (objectID == UUID.Zero) // User inventory
                {
                    IInventoryService invService = m_Scene.InventoryService;
                    InventoryItemBase assetRequestItem = new InventoryItemBase(notecard, user);
                    assetRequestItem = invService.GetItem(assetRequestItem);
                    if (assetRequestItem == null) // Library item
                    {
                        assetRequestItem = LibraryRootFolder.FindItem(notecard);

                        if (assetRequestItem != null) // Implicitly readable
                            return true;
                    }

                    // Notecards must be both mod and copy to be saveable
                    // This is because of they're not copy, you can't read
                    // them, and if they're not mod, well, then they're
                    // not mod. Duh.
                    //
                    if ((assetRequestItem.CurrentPermissions &
                            ((uint)PermissionMask.Modify |
                            (uint)PermissionMask.Copy)) !=
                            ((uint)PermissionMask.Modify |
                            (uint)PermissionMask.Copy))
                        return false;
                }
                else // Prim inventory
                {
                    SceneObjectPart part = scene.GetSceneObjectPart(objectID);

                    if (part == null)
                        return false;

                    if (part.OwnerID != user)
                        return false;

                    if ((part.OwnerMask & (uint)PermissionMask.Modify) == 0)
                        return false;

                    TaskInventoryItem ti = part.Inventory.GetInventoryItem(notecard);

                    if (ti == null)
                        return false;

                    if (ti.OwnerID != user)
                        return false;

                    // Require full perms
                    if ((ti.CurrentPermissions &
                            ((uint)PermissionMask.Modify |
                            (uint)PermissionMask.Copy)) !=
                            ((uint)PermissionMask.Modify |
                            (uint)PermissionMask.Copy))
                        return false;
                }

                return true;
            }

            private bool CanInstantMessage(UUID user, UUID target, Scene startScene)
            {
                return true;
            }

            private bool CanInventoryTransfer(UUID user, UUID target, Scene startScene)
            {
                return true;
            }

            private bool CanIssueEstateCommand(UUID user, Scene requestFromScene, bool ownerCommand)
            {
                if(ownerCommand)
                    return IsEstateOwner(user);

                return IsEstateManager(user);
            }

            private bool CanMoveObject(UUID objectID, UUID moverID, Scene scene)
            {
                return GenericObjectPermission(moverID, objectID, true);
            }

            private bool CanObjectEntry(UUID objectID, bool enteringRegion, Vector3 newPoint, Scene scene)
            {
                if ((newPoint.X > 257f || newPoint.X < -1f || newPoint.Y > 257f || newPoint.Y < -1f))
                {
                    return true;
                }

                SceneObjectPart rootPrim = m_Scene.GetSceneObjectPart(objectID);
                if (rootPrim == null)
                    return false;
                SceneObjectGroup task = rootPrim.ParentGroup;

                ILandObject land = m_Scene.LandChannel.GetLandObject(newPoint.X, newPoint.Y);

                if(!enteringRegion)
                {
                    ILandObject fromland = m_Scene.LandChannel.GetLandObject(task.AbsolutePosition.X, task.AbsolutePosition.Y);

                    if (fromland == land) // Not entering
                        return true;
                }

                if (land == null)
                {
                    m_log.DebugFormat("[PERMISSIONS] Denied prim because the land object at {0},{1} can't be found", task.AbsolutePosition.X, task.AbsolutePosition.Y);
                    return false;
                }

                if ((land.LandData.Flags & ((int)ParcelFlags.AllowAPrimitiveEntry)) != 0)
                {
                    return true;
                }

                if ((land.LandData.Flags & ((int)ParcelFlags.AllowGroupObjectEntry)) != 0)
                {
                    if (task.GroupID == land.LandData.GroupID)
                        return true;
                }

                // If we are EM, God, Owner, etc, allow it
                //
                if (GenericParcelPermission(task.OwnerID, newPoint, 0))
                    return true;

                //Otherwise, false!
                m_log.Debug("[PERMISSIONS] Denied prim because owner and group don't match");
                return false;
            }

            private bool CanReturnObjects(ILandObject land, UUID user, List<SceneObjectGroup> objects, Scene scene)
            {
                // Gods, estate owner and estate managers have blanket perms
                //
                if (IsEstateManager(user))
                    return true;

                GroupPowers powers;
                ILandObject l;

                ScenePresence sp = scene.GetScenePresence(user);
                if (sp == null) 
                    return false;

                IClientAPI client = sp.ControllingClient;

                foreach (SceneObjectGroup g in new List<SceneObjectGroup>(objects))
                {
                    // Any user can return their own objects at any time
                    //
                    if (g.OwnerID == user)
                        continue;

                    // This is a short cut for efficiency. If land is non-null,
                    // then all objects are on that parcel and we can save
                    // ourselves the checking for each prim. Much faster.
                    //
                    if (land != null)
                    {
                        l = land;
                    }
                    else
                    {
                        Vector3 pos = g.AbsolutePosition;

                        l = scene.LandChannel.GetLandObject(pos.X, pos.Y);
                    }

                    // If it's not over any land, then we can't do a thing
                    if (l == null)
                    {
                        objects.Remove(g);
                        continue;
                    }
                        
                    // If we own the land outright, then allow
                    //
                    if (l.LandData.OwnerID == user)
                        continue;

                    // Group voodoo
                    //
                    if (l.LandData.IsGroupOwned)
                    {
                        powers = (GroupPowers)client.GetGroupPowers(l.LandData.GroupID);
                        // Not a group member, or no rights at all
                        //
                        if (powers == (GroupPowers)0)
                        {
                            objects.Remove(g);
                            continue;
                        }

                        // Group deeded object?
                        //
                        if (g.OwnerID == l.LandData.GroupID &&
                            (powers & GroupPowers.ReturnGroupOwned) == (GroupPowers)0)
                        {
                            objects.Remove(g);
                            continue;
                        }

                        // Group set object?
                        //
                        if (g.GroupID == l.LandData.GroupID &&
                            (powers & GroupPowers.ReturnGroupSet) == (GroupPowers)0)
                        {
                            objects.Remove(g);
                            continue;
                        }

                        if ((powers & GroupPowers.ReturnNonGroup) == (GroupPowers)0)
                        {
                            objects.Remove(g);
                            continue;
                        }

                        // So we can remove all objects from this group land.
                        // Fine.
                        //
                        continue;
                    }

                    // By default, we can't remove
                    //
                    objects.Remove(g);
                }

                if (objects.Count == 0)
                    return false;

                return true;
            }

            private bool CanRezObject(int objectCount, UUID owner, Vector3 objectPosition, Scene scene)
            {
                bool permission = false;

                ILandObject land = m_Scene.LandChannel.GetLandObject(objectPosition.X, objectPosition.Y);
                if (land == null) return false;

                if ((land.LandData.Flags & ((int)ParcelFlags.CreateObjects)) ==
                    (int)ParcelFlags.CreateObjects)
                    permission = true;
                else
                    permission = CheckGroupPowers(owner, land.LandData.GroupID, (uint)GroupPowers.AllowRez);

                if (IsAdministrator(owner))
                {
                    permission = true;
                }

                if (GenericParcelPermission(owner, objectPosition, 0))
                {
                    permission = true;
                }

                return permission;
            }

            private bool CanRunConsoleCommand(UUID user, Scene requestFromScene)
            {
                return IsAdministrator(user);
            }

            private bool CanRunScript(UUID script, UUID objectID, UUID user, Scene scene)
            {
                return true;
            }

            private bool CanSellParcel(UUID user, ILandObject parcel, Scene scene)
            {
                if (parcel.LandData.IsGroupOwned)
                    return GenericParcelPermission(user, parcel, (ulong)GroupPowers.LandSetSale);
                else
                    return GenericParcelPermission(user, parcel, 0);
            }

            private bool CanTakeObject(UUID objectID, UUID stealer, Scene scene)
            {
                return GenericObjectPermission(stealer,objectID, false);
            }

        private bool CanTakeCopyObject(UUID objectID, UUID userID, Scene inScene)
        {
            if (IsGod(userID, inScene))
                return true;

            uint perms=GetEffectivePermissions(userID, objectID);

            if((perms & PERM_COPY) == 0)
            {
                ScenePresence presence = inScene.GetScenePresence(userID);
                if (presence != null)
                {
                    presence.ControllingClient.SendAgentAlertMessage("Copying this item has been denied by the permissions system", false);
                }
                return false;
            }

            SceneObjectPart part = inScene.GetSceneObjectPart(objectID);

            if (part == null)
                return false;

            SceneObjectGroup grp = part.ParentGroup;

            if (grp == null)
                return false;

            perms = grp.GetEffectivePermissions();
            if((perms & (PERM_COPY | PERM_TRANS)) != (PERM_COPY | PERM_TRANS))
            {
                ScenePresence presence = inScene.GetScenePresence(userID);
                if (presence != null)
                {
                    presence.ControllingClient.SendAgentAlertMessage("Copying this item has been denied by the permissions system", false);
                }
                return false;
            }

            return true;
        }

        private bool CanTerraformLand(UUID user, Vector3 position, Scene requestFromScene)
        {
            bool permission = false;

            // Estate override
            if (IsEstateManager(user))
                permission = true;

            float X = position.X;
            float Y = position.Y;

            if (X > 255)
                X = 255;
            if (Y > 255)
                Y = 255;
            if (X < 0)
                X = 0;
            if (Y < 0)
                Y = 0;

            // Land owner can terraform too
            ILandObject parcel = m_Scene.LandChannel.GetLandObject(X, Y);
            if (parcel != null)
            {
                if (parcel.LandData.IsGroupOwned)
                {
                    if(GenericParcelPermission(user, parcel, 0))
                        permission = true;
                }
                else
                {
                    if(GenericParcelPermission(user, parcel,
                            (ulong)GroupPowers.AllowEditLand))
                        permission = true;
                }

                if ((parcel.LandData.Flags & (uint)ParcelFlags.AllowTerraform) != 0)
                    permission = true;
            }

            return permission;
        }

        private bool CanViewScript(UUID script, UUID objectID, UUID user, Scene scene)
        {
            if (IsAdministrator(user))
                return true;

            if (objectID == UUID.Zero) // User inventory
            {
                IInventoryService invService = m_Scene.InventoryService;
                InventoryItemBase assetRequestItem = new InventoryItemBase(script, user);
                assetRequestItem = invService.GetItem(assetRequestItem);
                if (assetRequestItem == null) // Library item
                {
                    assetRequestItem = LibraryRootFolder.FindItem(script);

                    if (assetRequestItem != null) // Implicitly readable
                        return true;
                }

                // SL is rather harebrained here. In SL, a script you
                // have mod/copy no trans is readable. This subverts
                // permissions, but is used in some products, most
                // notably Hippo door plugin and HippoRent 5 networked
                // prim counter.
                // To enable this broken SL-ism, remove Transfer from
                // the below expressions.
                // Trying to improve on SL perms by making a script
                // readable only if it's really full perms
                //
                if ((assetRequestItem.CurrentPermissions &
                        ((uint)PermissionMask.Modify |
                        (uint)PermissionMask.Copy |
                        (uint)PermissionMask.Transfer)) !=
                        ((uint)PermissionMask.Modify |
                        (uint)PermissionMask.Copy |
                        (uint)PermissionMask.Transfer))
                    return false;
            }
            else // Prim inventory
            {
                SceneObjectPart part = scene.GetSceneObjectPart(objectID);

                if (part == null)
                    return false;

                if (part.OwnerID != user)
                    return false;

                if ((part.OwnerMask & (uint)PermissionMask.Modify) == 0)
                    return false;

                TaskInventoryItem ti = part.Inventory.GetInventoryItem(script);

                if (ti == null)
                    return false;

                if (ti.OwnerID != user)
                    return false;

                // Require full perms
                if ((ti.CurrentPermissions &
                        ((uint)PermissionMask.Modify |
                        (uint)PermissionMask.Copy |
                        (uint)PermissionMask.Transfer)) !=
                        ((uint)PermissionMask.Modify |
                        (uint)PermissionMask.Copy |
                        (uint)PermissionMask.Transfer))
                    return false;
            }

            return true;
        }

        private bool CanViewNotecard(UUID notecard, UUID objectID, UUID user, Scene scene)
        {
            if (IsAdministrator(user))
                return true;

            if (objectID == UUID.Zero) // User inventory
            {
                IInventoryService invService = m_Scene.InventoryService;
                InventoryItemBase assetRequestItem = new InventoryItemBase(notecard, user);
                assetRequestItem = invService.GetItem(assetRequestItem);

                if (assetRequestItem == null) // Library item
                {
                    assetRequestItem = LibraryRootFolder.FindItem(notecard);

                    if (assetRequestItem != null) // Implicitly readable
                        return true;
                }

                // Notecards are always readable unless no copy
                //
                if ((assetRequestItem.CurrentPermissions &
                        (uint)PermissionMask.Copy) !=
                        (uint)PermissionMask.Copy)
                    return false;
            }
            else // Prim inventory
            {
                SceneObjectPart part = scene.GetSceneObjectPart(objectID);

                if (part == null)
                    return false;

                if (part.OwnerID != user)
                    return false;

                if ((part.OwnerMask & (uint)PermissionMask.Modify) == 0)
                    return false;

                TaskInventoryItem ti = part.Inventory.GetInventoryItem(notecard);

                if (ti == null)
                    return false;

                if (ti.OwnerID != user)
                    return false;

                // Notecards are always readable unless no copy
                //
                if ((ti.CurrentPermissions &
                        (uint)PermissionMask.Copy) !=
                        (uint)PermissionMask.Copy)
                    return false;
            }

            return true;
        }
        #endregion

            public bool CanLinkObject(UUID userID, UUID objectID)
            {
                uint perms=GetEffectivePermissions(editorID, objectID);

                if((perms & PERM_MODIFY) == 0)
                    return false;

                return true;
            }

            public bool CanDelinkObject(UUID userID, UUID objectID)
            {
                uint perms=GetEffectivePermissions(editorID, objectID);

                if((perms & PERM_MODIFY) == 0)
                    return false;

                return true;
            }

            public bool CanBuyLand(UUID userID, ILandObject parcel, Scene scene)
            {
                return true;
            }

            public bool CanCopyObjectInventory(UUID itemID, UUID objectID, UUID userID)
            {
                return true;
            }

            public bool CanDeleteObjectInventory(UUID itemID, UUID objectID, UUID userID)
            {
                return true;
            }

            public bool CanCreateObjectInventory(int invType, UUID objectID, UUID userID)
            {
                return true;
            }

        public bool CanCreateUserInventory(int invType, UUID userID)
        {
            return true;            
        }
        
        public bool CanCopyUserInventory(UUID itemID, UUID userID)
        {
            return true;            
        }        
        
        public bool CanEditUserInventory(UUID itemID, UUID userID)
        {            
            return true;            
        }
        
        public bool CanDeleteUserInventory(UUID itemID, UUID userID)
        {
            return true;            
        }        

        public bool CanTeleport(UUID userID, Scene scene)
        {
            return true;
        }

        public bool BypassPermissions()
        {
                return false;
        }

        public bool PropagatePermissions()
        {
                return true;
        }

        public bool CanResetScript(UUID objectID, UUID itemID, UUID agentID, Scene scene)
        {
            return GenericObjectPermission(agentID, objectID, false);
        }
    }
}
