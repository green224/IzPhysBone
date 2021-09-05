﻿
using UnityEngine;
using Unity.Entities;
using Unity.Mathematics;
using System.Runtime.InteropServices;

namespace IzBone.IzBCollider.Core {
	using Common;

	/** コライダー１セットの最親につけるコンポーネント */
	public struct BodiesPack : IComponentData {
		public Entity first;
	}



	// 以降、１コライダーごとのEntityに対して付けるコンポーネント群
	public struct Body:IComponentData {}
	public struct Body_Next:IComponentData {public Entity value;}	// 次のコライダーへの参照
	public struct Body_ShapeType:IComponentData {public ShapeType value;}
	public struct Body_Center:IComponentData {public float3 value;}
	public struct Body_R:IComponentData {public float3 value;}
	public struct Body_Rot:IComponentData {public quaternion value;}

	[StructLayout(LayoutKind.Explicit)] public struct Body_RawCollider:IComponentData {
		[FieldOffset(0)] public Collider_Sphere sphere;
		[FieldOffset(0)] public Collider_Capsule capsule;
		[FieldOffset(0)] public Collider_Box box;
		[FieldOffset(0)] public Collider_Plane plane;

		/** 指定の位置・半径・ShapeTypeで衝突を解決する */
		public bool solveCollision(
			ShapeType shapeType,
			ref float3 pos,
			float r
		) {
			var sc = new Collider_Sphere{pos=pos, r=r};

			float3 n=0; float d=0; var isCol=false;
			unsafe {
				switch (shapeType) {
				case ShapeType.Sphere  : isCol = sphere.solve(&sc,&n,&d); break;
				case ShapeType.Capsule : isCol = capsule.solve(&sc,&n,&d); break;
				case ShapeType.Box     : isCol = box.solve(&sc,&n,&d); break;
				case ShapeType.Plane   : isCol = plane.solve(&sc,&n,&d); break;
				}
			}

			if (isCol) pos += n * d;
			return isCol;
		}
	}

	// BodyとBodyAuthoringとの橋渡し役を行うためのマネージドコンポーネント
	public sealed class Body_M2D:IComponentData {
		public BodyAuthoring bodyAuth;				//!< 生成元
	}

}
