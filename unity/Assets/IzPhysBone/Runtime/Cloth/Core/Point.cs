﻿using System;
using UnityEngine;

using Unity.Mathematics;
using static Unity.Mathematics.math;

using System.Runtime.InteropServices;


namespace IzPhysBone.Cloth.Core {
	
	/** シミュレート単位となるパーティクル1粒子分の情報 [32bytes] */
	[StructLayout(LayoutKind.Explicit)]
	public struct Point {
		[FieldOffset(0)] public Collider.Collider_Sphere col;
		[FieldOffset(16)] public Vector3 v;
		[FieldOffset(28)] public float invM;

		public Point(Vector3 pos, float m, float r) {
			col.pos = pos;
			col.r = r;
			v = new Vector3(0,0,0);
			invM = m < MinimumM ? 0 : (1f/m);
		}

		const float MinimumM = 0.00000001f;
	}

}
