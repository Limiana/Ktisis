using System;
using System.Collections.Generic;

using Dalamud.Hooking;
using Dalamud.Game.ClientState.Objects.Types;

using FFXIVClientStructs.FFXIV.Client.Graphics.Render;
using FFXIVClientStructs.Havok.Animation.Playback.Control.Default;
using FFXIVClientStructs.Havok.Animation.Rig;

using Ktisis.Structs;
using Ktisis.Structs.Actor;
using Ktisis.Structs.Poses;
using Ktisis.Util;

namespace Ktisis.Interop.Hooks {
	public static class PoseHooks {
		internal delegate ulong SetBoneModelSpaceFfxivDelegate(nint partialSkeleton, ushort boneId, nint transform, bool enableSecondary, bool enablePropagate);
		internal static Hook<SetBoneModelSpaceFfxivDelegate> SetBoneModelSpaceFfxivHook = null!;

		internal delegate nint CalculateBoneModelSpaceDelegate(ref hkaPose pose, int boneIdx);
		internal static Hook<CalculateBoneModelSpaceDelegate> CalculateBoneModelSpaceHook = null!;

		internal unsafe delegate void SyncModelSpaceDelegate(hkaPose* pose);
		internal static Hook<SyncModelSpaceDelegate> SyncModelSpaceHook = null!;

		internal unsafe delegate byte* LookAtIKDelegate(byte* a1, long* a2, long* a3, float a4, long* a5, long* a6);
		internal static Hook<LookAtIKDelegate> LookAtIKHook = null!;
		
		internal delegate nint KineDriverDelegate(nint a1, nint a2);
		internal static Hook<KineDriverDelegate> KineDriverHook = null!;

		internal unsafe delegate byte AnimFrozenDelegate(uint* a1, int a2);
		internal static Hook<AnimFrozenDelegate> AnimFrozenHook = null!;

		internal unsafe delegate void UpdatePosDelegate(Actor* a1);
		internal static Hook<UpdatePosDelegate> UpdatePosHook = null!;

		internal unsafe delegate char SetSkeletonDelegate(Skeleton* a1, ushort a2, nint a3);
		internal static Hook<SetSkeletonDelegate> SetSkeletonHook = null!;
		
		internal unsafe delegate nint BustDelegate(ActorModel* a1, Breasts* a2);
		internal static Hook<BustDelegate> BustHook = null!;

		internal static bool PosingEnabled { get; private set; }
		internal static bool AnamPosingEnabled => StaticOffsets.IsAnamPosing;

		internal static Dictionary<uint, PoseContainer> PreservedPoses = new();

		internal static PoseAutoSave AutoSave = new();

		internal static unsafe void Init() {
			var setBoneModelSpaceFfxiv = Services.SigScanner.ScanText("48 8B C4 48 89 58 18 55 56 57 41 54 41 55 41 56 41 57 48 81 EC ?? ?? ?? ?? 0F 29 70 B8 0F 29 78 A8 44 0F 29 40 ?? 44 0F 29 48 ?? 48 8B 05 ?? ?? ?? ?? 48 33 C4 48 89 84 24 ?? ?? ?? ?? 48 8B B1");
			SetBoneModelSpaceFfxivHook = Services.Hooking.HookFromAddress<SetBoneModelSpaceFfxivDelegate>(setBoneModelSpaceFfxiv, SetBoneModelSpaceFfxivDetour);

			var calculateBoneModelSpace = Services.SigScanner.ScanText("40 53 48 83 EC 10 4C 8B 49 28");
			CalculateBoneModelSpaceHook = Services.Hooking.HookFromAddress<CalculateBoneModelSpaceDelegate>(calculateBoneModelSpace, CalculateBoneModelSpaceDetour);

			var syncModelSpace = Services.SigScanner.ScanText("48 83 EC 18 80 79 38 00");
			SyncModelSpaceHook = Services.Hooking.HookFromAddress<SyncModelSpaceDelegate>(syncModelSpace, SyncModelSpaceDetour);

			var lookAtIK = Services.SigScanner.ScanText("48 8B C4 48 89 58 08 48 89 70 10 F3 0F 11 58");
			LookAtIKHook = Services.Hooking.HookFromAddress<LookAtIKDelegate>(lookAtIK, LookAtIKDetour);

			var kineDrive = Services.SigScanner.ScanText("48 8B C4 55 57 48 83 EC 58");
			KineDriverHook = Services.Hooking.HookFromAddress<KineDriverDelegate>(kineDrive, KineDriverDetour);
			
			var animFrozen = Services.SigScanner.ScanText("E8 ?? ?? ?? ?? 0F B6 F8 84 C0 74 12");
			AnimFrozenHook = Services.Hooking.HookFromAddress<AnimFrozenDelegate>(animFrozen, AnimFrozenDetour);
			
			var updatePos = Services.SigScanner.ScanText("E8 ?? ?? ?? ?? 84 DB 74 45");
			UpdatePosHook = Services.Hooking.HookFromAddress<UpdatePosDelegate>(updatePos, UpdatePosDetour);

			var loadSkele = Services.SigScanner.ScanText("E8 ?? ?? ?? ?? 48 C1 E5 08");
			SetSkeletonHook = Services.Hooking.HookFromAddress<SetSkeletonDelegate>(loadSkele, SetSkeletonDetour);
			SetSkeletonHook.Enable();

			var loadBust = Services.SigScanner.ScanText("E8 ?? ?? ?? ?? 0F 28 7C 24 ?? 0F 28 74 24 ?? 4C 8B 74 24 ??");
			BustHook = Services.Hooking.HookFromAddress<BustDelegate>(loadBust, BustDetour);
		}

		internal static void DisablePosing() {
			PreservedPoses.Clear();
			CalculateBoneModelSpaceHook?.Disable();
			SetBoneModelSpaceFfxivHook?.Disable();
			SyncModelSpaceHook?.Disable();
			LookAtIKHook?.Disable();
			KineDriverHook.Disable();
			UpdatePosHook?.Disable();
			AnimFrozenHook?.Disable();
			BustHook?.Disable();
			AutoSave?.Disable();
			PosingEnabled = false;
		}

		internal static void EnablePosing() {
			CalculateBoneModelSpaceHook?.Enable();
			SetBoneModelSpaceFfxivHook?.Enable();
			SyncModelSpaceHook?.Enable();
			LookAtIKHook?.Enable();
			KineDriverHook.Enable();
			UpdatePosHook?.Enable();
			AnimFrozenHook?.Enable();
			BustHook?.Enable();
			AutoSave?.Enable();
			PosingEnabled = true;
		}

		/// <summary>
		/// Toggles posing mode via hooks.
		/// </summary>
		/// <returns></returns>
		internal static bool TogglePosing() {
			if (PosingEnabled) {
				DisablePosing();
			} else {
				EnablePosing();
			}
			return PosingEnabled;
		}

		private static ulong SetBoneModelSpaceFfxivDetour(nint partialSkeleton, ushort boneId, nint transform, bool enableSecondary, bool enablePropagate) {
			if (AnamPosingEnabled)
				return SetBoneModelSpaceFfxivHook.Original(partialSkeleton, boneId, transform, enableSecondary, enablePropagate);

			return boneId;
		}

		private static unsafe nint CalculateBoneModelSpaceDetour(ref hkaPose pose, int boneIdx) {
			if (AnamPosingEnabled)
				return CalculateBoneModelSpaceHook.Original(ref pose, boneIdx);

			// This is expected to return the hkQsTransform at the given index in the pose's ModelSpace transform array.
			return (nint)(pose.ModelPose.Data + boneIdx);
		}

		private static unsafe void SyncModelSpaceDetour(hkaPose* pose) {
			var call = AnamPosingEnabled;

			if (!Ktisis.IsInGPose && PosingEnabled) {
				DisablePosing();
				call = true;
			}

			if (call)
				SyncModelSpaceHook.Original(pose);
		}

		private unsafe static void UpdatePosDetour(Actor* a1) {

		}

		private static unsafe char SetSkeletonDetour(Skeleton* a1, ushort a2, nint a3) {
			var exec = SetSkeletonHook.Original(a1, a2, a3);
			if (!PosingEnabled && !AnamPosingEnabled) return exec;

			try {
				var partial = a1->PartialSkeletons[a2];
				var pose = partial.GetHavokPose(0);
				if (pose == null) return exec;

				if (a3 == nint.Zero) {
					if (a2 == 0) {
						// TODO: Any way to do this without iterating the object table?
						foreach (var obj in Services.ObjectTable) {
							var actor = (Actor*)obj.Address;
							if (actor->Model == null || actor->Model->Skeleton != a1) continue;

							PoseContainer container = new();
							container.Store((FFXIVClientStructs.FFXIV.Client.Graphics.Render.Skeleton*)actor->Model->Skeleton);
							PreservedPoses[actor->ObjectID] = container;
						}
					}

					return exec;
				}

				if (!AnamPosingEnabled)
					SyncModelSpaceHook.Original(pose);

				// Make sure new partials get parented properly
				if (a2 > 0)
					a1->ParentPartialToRoot(a2);

				if (a2 < 3) {
					foreach (var obj in Services.ObjectTable) {
						var actor = (Actor*)obj.Address;
						if (actor->Model == null || actor->Model->Skeleton != a1) continue;

						if (actor->RenderMode == RenderMode.Draw) break;

						if (a2 == 0)
							UpdatePosHook.Original(actor);

						if (PreservedPoses.TryGetValue(actor->ObjectID, out var backup)) {
							var trans = PoseTransforms.Rotation;
							if (AnamPosingEnabled) {
								if (StaticOffsets.IsPositionFrozen) trans |= PoseTransforms.Position;
								if (StaticOffsets.IsScalingFrozen) trans |= PoseTransforms.Scale;
							}
							backup.ApplyToPartial(a1, a2, trans, true, !AnamPosingEnabled);
						}
					}
				}
			} catch (Exception e) {
				Logger.Error(e, "Error in SetSkeletonDetour.");
			}

			return exec;
		}

		private unsafe static nint BustDetour(ActorModel* a1, Breasts* a2) {
			var exec = BustHook.Original(a1, a2);
			a1->ScaleBust(true);
			return exec;
		}

		private unsafe static byte* LookAtIKDetour(byte* a1, long* a2, long* a3, float a4, long* a5, long* a6) {
			return (byte*)nint.Zero;
		}

		private static nint KineDriverDetour(nint a1, nint a2) {
			return nint.Zero;
		}

		private unsafe static byte AnimFrozenDetour(uint* a1, int a2) {
			return 1;
		}

		private static unsafe void SyncBone(hkaPose* bonesPose, int index) {
			CalculateBoneModelSpaceHook.Original(ref *bonesPose, index);
		}

		public static unsafe bool IsGamePlaybackRunning(IGameObject? gPoseTarget) {
			var animationControl = GetAnimationControl(gPoseTarget);
			if (animationControl == null) return true;
			return animationControl->PlaybackSpeed == 1;
		}

		public static unsafe hkaDefaultAnimationControl* GetAnimationControl(IGameObject? go) {
			if (go == null) return null;

			var actor = (Actor*)go.Address;
			if (actor->Model == null ||
				actor->Model->Skeleton == null ||
				actor->Model->Skeleton->PartialSkeletons == null ||
				actor->Model->Skeleton->PartialSkeletons->GetHavokAnimatedSkeleton(0) == null ||
				actor->Model->Skeleton->PartialSkeletons->GetHavokAnimatedSkeleton(0)->AnimationControls.Length == 0 ||
				actor->Model->Skeleton->PartialSkeletons->GetHavokAnimatedSkeleton(0)->AnimationControls[0].Value == null)
				return null;

			return actor->Model->Skeleton->PartialSkeletons->GetHavokAnimatedSkeleton(0)->AnimationControls[0];
		}

		internal static void Dispose() {
			SetBoneModelSpaceFfxivHook.Disable();
			SetBoneModelSpaceFfxivHook.Dispose();
			CalculateBoneModelSpaceHook.Disable();
			CalculateBoneModelSpaceHook.Dispose();
			SyncModelSpaceHook.Disable();
			SyncModelSpaceHook.Dispose();
			LookAtIKHook.Disable();
			LookAtIKHook.Dispose();
			KineDriverHook.Disable();
			KineDriverHook.Dispose();
			AnimFrozenHook.Disable();
			AnimFrozenHook.Dispose();
			UpdatePosHook.Disable();
			UpdatePosHook.Dispose();
			SetSkeletonHook.Disable();
			SetSkeletonHook.Dispose();
			BustHook.Disable();
			BustHook.Dispose();
		}
	}
}
