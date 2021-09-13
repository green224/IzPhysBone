﻿//#define WITH_DEBUG
using UnityEngine.Jobs;
using Unity.Jobs;

using Unity.Burst;
using Unity.Entities;
using Unity.Collections;
using Unity.Mathematics;
using static Unity.Mathematics.math;
using System.Runtime.CompilerServices;



namespace IzBone.PhysSpring.Core {
using Common;

[UpdateInGroup(typeof(IzBoneSystemGroup))]
[UpdateAfter(typeof(IzBCollider.Core.IzBColliderSystem))]
[AlwaysUpdateSystem]
public sealed class IzBPhysSpringSystem : SystemBase {

	// BodyAuthoringを登録・登録解除する処理
	internal void register(RootAuthoring auth, EntityRegisterer.RegLink regLink)
		=> _entityReg.register(auth, regLink);
	internal void unregister(RootAuthoring auth, EntityRegisterer.RegLink regLink)
		=> _entityReg.unregister(auth, regLink);
	internal void resetParameters(EntityRegisterer.RegLink regLink)
		=> _entityReg.resetParameters(regLink);
	EntityRegisterer _entityReg;

	/** 指定のRootAuthの物理状態をリセットする */
	internal void reset(EntityRegisterer.RegLink regLink) {
		var etp = _entityReg.etPacks;
		for (int i=0; i<regLink.etpIdxs.Count; ++i) {
			var etpIdx = regLink.etpIdxs[i];
			var e = etp.Entities[ etpIdx ];
			var t = etp.Transforms[ etpIdx ];

			var spring = GetComponent<Ptcl>(e);
			spring.spring_rot.v = 0;
			spring.spring_sft.v = 0;
			SetComponent(e, spring);

			var defState = GetComponent<Ptcl_DefState>(e);
			var childWPos = t.localToWorldMatrix.MultiplyPoint(defState.childDefPos);

			var wPosCache = GetComponent<Ptcl_LastWPos>(e);
			wPosCache.value = childWPos;
			SetComponent(e, wPosCache);
		}
	}

	/** 現在のTransformをすべてのECSへ転送する処理 */
#if WITH_DEBUG
	struct MngTrans2ECSJob
#else
	[BurstCompile]
	struct MngTrans2ECSJob : IJobParallelForTransform
#endif
	{
		[ReadOnly] public NativeArray<Entity> entities;

		[NativeDisableParallelForRestriction]
		[WriteOnly] public ComponentDataFromEntity<CurTrans> curTranss;
		[NativeDisableParallelForRestriction]
		public ComponentDataFromEntity<Root> roots;

#if WITH_DEBUG
		public void Execute(int index, UnityEngine.Transform transform)
#else
		public void Execute(int index, TransformAccess transform)
#endif
		{
			var entity = entities[index];

			curTranss[entity] = new CurTrans{
				lPos = transform.localPosition,
				lRot = transform.localRotation,
				lScl = transform.localScale,
			};
			if (roots.HasComponent(entity)) {
				// 最親の場合はL2Wも同期
				var a = roots[entity];
				a.l2w = transform.localToWorldMatrix;
				roots[entity] = a;
			}
		}
	}

	/** ECSで得た結果の回転を、マネージドTransformに反映させる処理 */
#if WITH_DEBUG
	struct SpringTransUpdateJob
#else
	[BurstCompile]
	struct SpringTransUpdateJob : IJobParallelForTransform
#endif
	{
		[ReadOnly] public NativeArray<Entity> entities;
		[ReadOnly] public ComponentDataFromEntity<CurTrans> curTranss;

#if WITH_DEBUG
		public void Execute(int index, UnityEngine.Transform transform)
#else
		public void Execute(int index, TransformAccess transform)
#endif
		{
			var entity = entities[index];
			var curTrans = curTranss[entity];

			// 適応
			transform.localPosition = curTrans.lPos;
			transform.localRotation = curTrans.lRot;
		}
	}


	protected override void OnCreate() {
		_entityReg = new EntityRegisterer();
	}

	protected override void OnDestroy() {
		_entityReg.Dispose();
	}

	override protected void OnUpdate() {
//		var deltaTime = World.GetOrCreateSystem<Time8.TimeSystem>().DeltaTime;
		var deltaTime = Time.DeltaTime;

		// 追加・削除されたAuthの情報をECSへ反映させる
		_entityReg.apply(EntityManager);


		{// 風の影響を決定する処理。
			// TODO : これは今はWithoutBurstだが、後で何とかする
			Entities.ForEach((
				Entity entity,
				in Root_M2D rootM2D
			)=>{
				SetComponent(entity, new Root_Air{
					winSpd = rootM2D.auth.windSpeed,
					airDrag = rootM2D.auth.airDrag,
				});
			}).WithoutBurst().Run();
		}


		{// 重力加速度を決定する処理
			var defG = (float3)UnityEngine.Physics.gravity;
			Dependency = Entities.ForEach((ref Root_G g)=>{
				g.value = g.src.evaluate(defG);
			}).Schedule( Dependency );
		}


		// 現在のTransformをすべてのECSへ転送
		var etp = _entityReg.etPacks;
		if (etp.Length != 0) {
		#if WITH_DEBUG
			var a = new MngTrans2ECSJob{
		#else
			Dependency = new MngTrans2ECSJob{
		#endif
				entities = etp.Entities,
				curTranss = GetComponentDataFromEntity<CurTrans>(false),
				roots = GetComponentDataFromEntity<Root>(false),
		#if WITH_DEBUG
			};
			for (int i=0; i<etp.Transforms.length; ++i) {
				a.Execute( i, etp.Transforms[i] );
			}
		#else
			}.Schedule( etp.Transforms, Dependency );
		#endif
		}


		// デフォルト姿勢等を更新する処理。
		// 現在位置をデフォルト位置として再計算する必要がある場合は、
		// このタイミングで再計算を行う
	#if WITH_DEBUG
		Entities.ForEach((
	#else
		Dependency = Entities.ForEach((
	#endif
#if false
			Entity entity,
			ref DefaultState defState,
			in CurTrans curTrans,
			in Ptcl_Child child,
			in Ptcl_Root root
		)=>{
			if (!GetComponent<Root_WithAnimation>(root.value).value) return;

			var childTrans = GetComponent<CurTrans>(child.value);

			// 初期位置情報を更新
			defState.defRot = curTrans.lRot;
			defState.defPos = curTrans.lPos;
			defState.childDefPos = childTrans.lPos;
			defState.childDefPosMPR = mul(curTrans.lRot, curTrans.lScl * childTrans.lPos);
#else
			Entity entity,
			in Root_WithAnimation withAnimation
		)=>{
			if (!withAnimation.value) return;

			// WithAnimの場合のみ、Root以下の全Ptclに対して処理
			entity = GetComponent<Root_FirstPtcl>(entity).value;
			var curTrans = GetComponent<CurTrans>(entity);
			while (true) {
				if (!HasComponent<Ptcl_Child>(entity)) break;
				var childEntity = GetComponent<Ptcl_Child>(entity).value;
				var childTrans = GetComponent<CurTrans>(childEntity);

				// 初期位置情報を更新
				var defState = GetComponent<Ptcl_DefState>(entity);
				defState.defRot = curTrans.lRot;
				defState.defPos = curTrans.lPos;
				defState.childDefPos = childTrans.lPos;
				defState.childDefPosMPR = mul(curTrans.lRot, curTrans.lScl * childTrans.lPos);
				SetComponent(entity, defState);

				// ループを次に進める
				entity = childEntity;
				curTrans = childTrans;
			}
#endif
	#if WITH_DEBUG
		}).WithoutBurst().Run();
	#else
		}).Schedule(Dependency);
	#endif


// コライダ衝突処理はECS的にはこうやって分離するべきだが、分離してみたところ逆に処理負荷が増えているので、いったんコメントアウト
//		// コライダの衝突判定
//	#if WITH_DEBUG
//		Entities.ForEach((
//	#else
//		Dependency = Entities.ForEach((
//	#endif
//			ref Ptcl_LastWPos lastWPos,
//			in Ptcl_R r,
//			in Ptcl_Child child,
//			in Ptcl_Root root
//		)=>{
//			var collider = GetComponent<Root_ColliderPack>(root.value).value;
//			if (collider == Entity.Null) return;
//
//			// 前フレームにキャッシュされた位置にパーティクルが移動したとして、
//			// その位置でコライダとの衝突解決をしておく
//
//			var rVal = r.value;
//			for (
//				var e = collider;
//				e != Entity.Null;
//				e = GetComponent<IzBCollider.Core.Body_Next>(e).value
//			) {
//				var rc = GetComponent<IzBCollider.Core.Body_RawCollider>(e);
//				var st = GetComponent<IzBCollider.Core.Body_ShapeType>(e).value;
//				rc.solveCollision( st, ref lastWPos.value, rVal );
//			}
//	#if WITH_DEBUG
//		}).WithoutBurst().Run();
//	#else
//		}).Schedule(Dependency);
//	#endif


		{// シミュレーションの本更新処理
		var particles = GetComponentDataFromEntity<Ptcl>();
		var lastWPoss = GetComponentDataFromEntity<Ptcl_LastWPos>();
		var curTranss = GetComponentDataFromEntity<CurTrans>();
	#if WITH_DEBUG
		Entities.ForEach((
	#else
		Dependency = Entities
		.WithNativeDisableParallelForRestriction(particles)
		.WithNativeDisableParallelForRestriction(lastWPoss)
		.WithNativeDisableParallelForRestriction(curTranss)
		.ForEach((
	#endif
			Entity entity,
			in Root root
		)=>{
			// 一繋ぎ分のSpringの情報をまとめて取得しておく
			var buf_particle    = new NativeArray<Ptcl>(root.depth, Allocator.Temp);
			var buf_defState  = new NativeArray<Ptcl_DefState>(root.depth, Allocator.Temp);
			var buf_lastWPos  = new NativeArray<Ptcl_LastWPos>(root.depth, Allocator.Temp);
			var buf_curTrans  = new NativeArray<CurTrans>(root.depth, Allocator.Temp);
			var buf_entity    = new NativeArray<Entity>(root.depth, Allocator.Temp);
			{
				var e = GetComponent<Root_FirstPtcl>(entity).value;
				for (int i=0;; ++i) {
					buf_entity[i]    = e;
					buf_particle[i]    = particles[e];
					buf_defState[i]  = GetComponent<Ptcl_DefState>(e);
					buf_lastWPos[i] = lastWPoss[e];
					buf_curTrans[i]  = curTranss[e];
					if (i == root.depth-1) break;
					e = GetComponent<Ptcl_Child>(e).value;
				}
			}

			// 本更新処理
			var iterationNum = root.iterationNum;
			var rsRate = root.rsRate;
			var dt = deltaTime / iterationNum;
			var collider = GetComponent<Root_ColliderPack>(entity).value;
			for (int itr=0; itr<iterationNum; ++itr) {
				var l2w = root.l2w;
				for (int i=0; i<root.depth; ++i) {

					// OneSpringごとのコンポーネントを取得
					var spring = buf_particle[i];
					var defState = buf_defState[i];
					var lastWPos = buf_lastWPos[i];

					// 前フレームにキャッシュされた位置にパーティクルが移動したとして、
					// その位置でコライダとの衝突解決をしておく
					if (collider != Entity.Null) {
						var r = GetComponent<Ptcl_R>(buf_entity[i]).value;
						for (
							var e = collider;
							e != Entity.Null;
							e = GetComponent<IzBCollider.Core.Body_Next>(e).value
						) {
							var rc = GetComponent<IzBCollider.Core.Body_RawCollider>(e);
							var st = GetComponent<IzBCollider.Core.Body_ShapeType>(e).value;
							rc.solveCollision( st, ref lastWPos.value, r );
						}
					}

					// 前フレームにキャッシュされた位置を先端目標位置として、
					// 先端目標位置へ移動した結果の移動・姿勢ベクトルを得る
					float3 sftVec, rotVec;
					{
						// ワールド座標をボーンローカル座標に変換する
//						var lastLPos = lastWPos.value - ppL2W.c3.xyz;
//						lastLPos = float3(
//							dot(lastLPos, ppL2W.c0.xyz),
//							dot(lastLPos, ppL2W.c1.xyz),
//							dot(lastLPos, ppL2W.c2.xyz)
//						);
//						var tgtBPos = lastLPos - defState.defPos;
						var w2l = inverse(l2w);
						var tgtBPos = mulMxPos(w2l, lastWPos.value) - defState.defPos;

						// 移動と回転の影響割合を考慮してシミュレーション結果を得る
						var cdpMPR = defState.childDefPosMPR;
						if ( rsRate < 0.001f ) {
							rotVec = getRotVecFromTgtBPos( tgtBPos, ref spring, cdpMPR );
							sftVec = Unity.Mathematics.float3.zero;
						} else if ( 0.999f < rsRate ) {
							rotVec = Unity.Mathematics.float3.zero;
							sftVec = spring.range_sft.local2global(tgtBPos - cdpMPR);
						} else {
							rotVec = getRotVecFromTgtBPos( tgtBPos, ref spring, cdpMPR ) * (1f - rsRate);
							sftVec = spring.range_sft.local2global((tgtBPos - cdpMPR) * rsRate);
						}
					}

					// バネ振動を更新
					if ( rsRate <= 0.999f ) {
						spring.spring_rot.x = rotVec;
						spring.spring_rot.update(dt);
						rotVec = spring.spring_rot.x;
					}
					if ( 0.001f <= rsRate ) {
						spring.spring_sft.x = sftVec;
						spring.spring_sft.update(dt);
						sftVec = spring.spring_sft.x;
					}

					// 現在の姿勢情報から、Transformに設定するための情報を構築
					quaternion rot;
					float3 trs;
					if ( rsRate <= 0.999f ) {
						var theta = length(rotVec);
						if ( theta < 0.001f ) {
							rot = defState.defRot;
						} else {
							var axis = rotVec / theta;
							var q = Unity.Mathematics.quaternion.AxisAngle(axis, theta);
							rot = mul(q, defState.defRot);
						}
					} else {
						rot = defState.defRot;
					}
					if ( 0.001f <= rsRate ) {
						trs = defState.defPos + sftVec;
					} else {
						trs = defState.defPos;
					}
					var result = buf_curTrans[i];
					result.lRot = rot;
					result.lPos = trs;


					// L2W行列をここで再計算する。
					// このL2WはTransformには直接反映されないので、2重計算になってしまうが、
					// 親から順番に処理を進めないといけないし、Transformへの値反映はここからは出来ないので
					// 仕方なくこうしている。
					{
						var rotMtx = new float3x3(rot);
						var scl = result.lScl;
						var l2p = float4x4(
							float4( rotMtx.c0*scl.x, 0 ),
							float4( rotMtx.c1*scl.y, 0 ),
							float4( rotMtx.c2*scl.z, 0 ),
							float4( trs, 1 )
						);
						l2w = mul(l2w, l2p);
					}

					// 現在のワールド位置を保存
					// これは正確なChildの現在位置ではなく、位置移動のみ考慮から外している。
					// 位置移動が入っている正確な現在位置で計算すると、位置Spring計算が正常に出来ないためである。
					lastWPos.value = mulMxPos(l2w, defState.childDefPos);

					// バッファを更新
					buf_particle[i] = spring;
					buf_curTrans[i] = result;
					buf_lastWPos[i] = lastWPos;
				}
			}


			// コンポーネントへ値を反映
			for (int i=0; i<root.depth; ++i) {
				var e = buf_entity[i];
				particles[e] = buf_particle[i];
				curTranss[e] = buf_curTrans[i];
				lastWPoss[e] = buf_lastWPos[i];
			}
			buf_particle.Dispose();
			buf_defState.Dispose();
			buf_lastWPos.Dispose();
			buf_curTrans.Dispose();
			buf_entity.Dispose();

	#if WITH_DEBUG
		}).WithoutBurst().Run();
	#else
		}).ScheduleParallel(Dependency);
	#endif
		}



		// マネージド空間へ、結果を同期する
		// 本当はこれは並列にするまでもないが、
		// IJobParallelForTransformを使用しないとそもそもスレッド化できず、
		// そうなるとECSに乗せる事自体が瓦解するので、仕方なくこうしている
		if (etp.Length != 0) {
		#if WITH_DEBUG
			var a = new SpringTransUpdateJob{
		#else
			Dependency = new SpringTransUpdateJob{
		#endif
				entities = etp.Entities,
				curTranss = GetComponentDataFromEntity<CurTrans>(true),
		#if WITH_DEBUG
			};
			for (int i=0; i<etp.Transforms.length; ++i) {
				a.Execute( i, etp.Transforms[i] );
			}
		#else
			}.Schedule( etp.Transforms, Dependency );
		#endif
		}

	}

	/** 同次変換行列に位置を掛けて変換後の位置を得る処理 */
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	static float3 mulMxPos(float4x4 mtx, float3 pos) => mul( mtx, float4(pos,1) ).xyz;

	// 先端目標位置へ移動した結果の姿勢ベクトルを得る処理
	static float3 getRotVecFromTgtBPos(
		float3 tgtBPos,
		ref Ptcl spring,
		float3 childDefPosMPR
	) {
		var childDefDir = normalize( childDefPosMPR );
		float3 ret;
		tgtBPos = normalize(tgtBPos);
		
		var crs = cross(childDefDir, tgtBPos);
		var crsNrm = length(crs);
		if ( crsNrm < 0.001f ) {
			ret = Unity.Mathematics.float3.zero;
		} else {
			var theta = acos( dot(childDefDir, tgtBPos) );
			theta = spring.range_rot.local2global( theta );
			ret = crs * (theta/crsNrm);
		}

		return ret;
	}

}
}
