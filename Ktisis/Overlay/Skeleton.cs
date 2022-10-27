﻿using System;
using System.Numerics;

using ImGuiNET;

using FFXIVClientStructs.Havok;
using static FFXIVClientStructs.Havok.hkaPose;
using ActorSkeleton = FFXIVClientStructs.FFXIV.Client.Graphics.Render.Skeleton;

using Ktisis.Structs;
using Ktisis.Structs.Actor;
using Ktisis.Structs.Bones;

namespace Ktisis.Overlay {
	public static class Skeleton {
		// Allow other classes to retrieve the currently selected bone.
		// OverlayWindow sets Active and Update to false before every Draw call.
		// Changing these values will not change the selection, that's based on the Gizmo's owner ID.
		public static (
			bool Active, // A bone is currently selected.
			bool Update, // Signal to any class that caches transforms, that they need to be updated.
			int Partial,
			int Index,
			string Name
		) BoneSelect;

		public static bool IsBoneSelected(Bone bone) => BoneSelect.Active && BoneSelect.Partial == bone.Partial && BoneSelect.Index == bone.Index;

		public static void Toggle() {
			var visible = !Ktisis.Configuration.ShowSkeleton;
			Ktisis.Configuration.ShowSkeleton = visible;
			if (!visible && BoneSelect.Active) {
				BoneSelect.Active = false;
				OverlayWindow.SetGizmoOwner(null);
			}
		}

		public unsafe static void Draw() {
			// Fetch actor, model & skeleton

			if (Ktisis.GPoseTarget == null) return;

			var actor = (Actor*)Ktisis.GPoseTarget!.Address;
			var model = actor->Model;
			if (model == null) return;

			var matrix = Interop.Alloc.GetMatrix();

			// ImGui rendering

			var draw = ImGui.GetWindowDrawList();

			// Make selectable model root

			{
				var actorName = actor->GetNameOr("Actor");
				var gizmo = OverlayWindow.GetGizmo(actorName);
				if (gizmo != null) {
					model->Transform.get4x4ColumnMajor(&matrix.M11);
					gizmo.Matrix = matrix;
					if (gizmo.Draw()) {
						matrix = gizmo.Matrix;
						model->Transform.set((hkMatrix4f*)&matrix);
					}
				} else {
					Dalamud.GameGui.WorldToScreen(model->Position, out var pos2d);
					if (Selection.AddItem(actorName, pos2d).IsClicked())
						OverlayWindow.SetGizmoOwner(actorName);
				}
			}

			// Draw skeleton

			for (var p = 0; p < model->Skeleton->PartialSkeletonCount; p++) {
				var partial = model->Skeleton->PartialSkeletons[p];
				var pose = partial.GetHavokPose(0);
				if (pose == null) continue;

				var skeleton = pose->Skeleton;
				for (var i = 1; i < skeleton->Bones.Length; i++) {
					var bone = model->Skeleton->GetBone(p, i);
					var boneName = bone.HkaBone.Name.String;
					var parentId = bone.ParentId;

					var uniqueName = bone.UniqueName;

					if (!Ktisis.Configuration.IsBoneVisible(bone))
						continue; // Bone is hidden, move onto the next one.

					// Access bone transform
					var transform = bone.AccessModelSpace(PropagateOrNot.Propagate);

					// Get bone color and screen position
					var boneColor = ImGui.GetColorU32(Ktisis.Configuration.GetCategoryColor(bone));
					Dalamud.GameGui.WorldToScreen(bone.GetWorldPos(model), out var pos2d);

					// Draw line to bone parent if any
					if (parentId > 0) {
						// TODO: Draw lines for parents of partials.

						var parent = model->Skeleton->GetBone(p, parentId);

						var lineThickness = Math.Max(0.01f, Ktisis.Configuration.SkeletonLineThickness / Dalamud.Camera->Camera->InterpDistance * 2f);
						Dalamud.GameGui.WorldToScreen(parent.GetWorldPos(model), out var parentPos2d);
						draw.AddLine(pos2d, parentPos2d, boneColor, lineThickness);
					}

					// Create selectable item
					// TODO: Hide when moving gizmo?
					if (!IsBoneSelected(bone) && (boneName != "j_ago" || p == 0)) {
						var item = Selection.AddItem(uniqueName, pos2d, boneColor);
						if (item.IsClicked()) {
							BoneSelect.Update = true;
							BoneSelect.Name = boneName;
							OverlayWindow.SetGizmoOwner(uniqueName);
						}
					}

					// Bone selection & gizmo
					var gizmo = OverlayWindow.GetGizmo(uniqueName);
					if (gizmo != null) {
						transform->get4x4ColumnMajor(&matrix.M11);

						// Apply the root transform of the actor's model.
						// This is important for the gizmo's orientation to show correctly.
						matrix.Translation *= model->Height * model->Scale;
						gizmo.Matrix = Matrix4x4.Transform(matrix, model->Rotation);
						gizmo.Matrix.Translation += model->Position;

						// Draw the gizmo. This returns true if it has been moved.
						if (gizmo.Draw()) {		
							// Reverse the previous transform we did.
							gizmo.Matrix.Translation -= model->Position;
							matrix = Matrix4x4.Transform(gizmo.Matrix, Quaternion.Inverse(model->Rotation));
							matrix.Translation /= model->Height * model->Scale;

							// Write our updated matrix to memory.
							transform->set((hkMatrix4f*)&matrix);

							BoneSelect.Update = true;
						}

						BoneSelect.Active = true;
						BoneSelect.Partial = p;
						BoneSelect.Index = i;
					} else if (BoneSelect.Active && BoneSelect.Name == boneName) {
						// this is janky. as far as I'm aware this only exists for the jaw bone?
						var parent = GetSelectedBone(model->Skeleton)!;
						*transform = parent.Transform;
					}
				}
			}
		}

		public unsafe static Bone? GetSelectedBone(ActorSkeleton* skeleton) {
			if (!BoneSelect.Active) return null;

			var target = Ktisis.GPoseTarget;
			if (target == null) return null;

			var model = ((Actor*)target.Address)->Model;
			if (model == null) return null;

			return model->Skeleton->GetBone(BoneSelect.Partial, BoneSelect.Index);
		}
	}
}