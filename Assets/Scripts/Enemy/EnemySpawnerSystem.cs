using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.Rendering;

namespace Roguelike.DOTS
{
    /// <summary>
    /// System that manages enemy spawning at screen edges, at the rate set by the WaveDirector or the BenchmarkController.
    /// Runs on the main thread to access Unity's Camera and rendering systems.
    /// Instantiates enemy waves with random spawn positions around viewport boundaries.
    /// Runs before the systems that record commands on enemies and experience orbs: the clear and trim paths
    /// destroy entities immediately, which would otherwise invalidate commands still pending in the command buffer.
    /// Also runs before the TransformSystemGroup, so that its structural changes do not force the transform jobs
    /// of the current frame to complete early.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(TransformSystemGroup))]
    [UpdateBefore(typeof(ExperienceMovementSystem))]
    [UpdateBefore(typeof(EnemyHashSystem))]
    public partial struct EnemySpawnerSystem : ISystem
    {
        /// <summary>
        /// Cached batch mesh ID for rendering spawned enemies.
        /// Registered once during first update and reused for all enemies.
        /// </summary>
        private BatchMeshID mMeshID;
        
        /// <summary>
        /// Cached batch material ID for rendering spawned enemies.
        /// Registered once during first update and reused for all enemies.
        /// </summary>
        private BatchMaterialID mMaterialID;

        /// <summary>
        /// Cached batch mesh and material IDs of the elite prefab, when the spawner has one.
        /// </summary>
        private BatchMeshID mEliteMeshID;
        private BatchMaterialID mEliteMaterialID;
        private bool mHasElite;

        /// <summary>
        /// Cached batch mesh and material IDs of the runner prefab, when the spawner has one.
        /// </summary>
        private BatchMeshID mRunnerMeshID;
        private BatchMaterialID mRunnerMaterialID;
        private bool mHasRunner;

        /// <summary>
        /// Cached batch mesh and material IDs of the boss prefab, when the spawner has one.
        /// </summary>
        private BatchMeshID mBossMeshID;
        private BatchMaterialID mBossMaterialID;
        private bool mHasBoss;
        private bool mInitialized;

        /// <summary>
        /// Regular enemy prefab whose rendering is registered. The subscene is reloaded with the scene, which gives
        /// new prefab entities, and in a build new materials and meshes: a different prefab means a new subscene.
        /// </summary>
        private Entity mRegisteredPrefab;
        private EntityQuery mEnemyQuery;
        /// <summary>
        /// Enemies not yet queued for destruction. Only used for immediate destruction paths, because filtering on
        /// an enableable component forces a sync with the jobs writing it.
        /// </summary>
        private EntityQuery mAliveEnemyQuery;
        private EntityQuery mExperienceQuery;

        /// <summary>
        /// Initializes the system and sets up requirements.
        /// </summary>
        public void OnCreate(ref SystemState pState)
        {
            // Require the spawner data to be present
            pState.RequireForUpdate<EnemySpawnerData>();

            mEnemyQuery = SystemAPI.QueryBuilder().WithAll<EnemySpeedComponent>().Build();
            mAliveEnemyQuery = SystemAPI.QueryBuilder().WithAll<EnemySpeedComponent>().WithDisabled<EnemyDeadTag>().Build();
            mExperienceQuery = SystemAPI.QueryBuilder().WithAll<ExperienceData>().Build();
        }

        /// <summary>
        /// Main update loop that handles spawning logic each frame.
        /// Checks spawn timer, calculates screen edge positions, and instantiates enemy waves.
        /// </summary>
        public void OnUpdate(ref SystemState pState)
        {
            // --- Scene reload ---
            // The enemies and orbs of the previous session survive in the world, but their materials were unloaded
            // with the old subscene: remove them, and register the rendering of the new prefabs
            if (mInitialized && SystemAPI.TryGetSingleton(out EnemySpawnerData lCurrentSpawner)
                && lCurrentSpawner.mPrefab != mRegisteredPrefab)
            {
                pState.EntityManager.DestroyEntity(mEnemyQuery);
                pState.EntityManager.DestroyEntity(mExperienceQuery);
                UnregisterRendering(ref pState);
                mInitialized = false;
            }

            // --- Benchmark Clear Processing ---
            if (BenchmarkController.ConsumeClearRequest())
            {
                // Enemies already flagged dead are left to the EnemyDeathSystem
                pState.EntityManager.DestroyEntity(mAliveEnemyQuery);
                pState.EntityManager.DestroyEntity(mExperienceQuery);
                EnemyCounterSystem.TotalEnemies = 0;
                EnemyCounterSystem.VisibleEnemies = 0;

                var lHashHandle = pState.WorldUnmanaged.GetExistingUnmanagedSystem<EnemyHashSystem>();
                if (lHashHandle != SystemHandle.Null)
                {
                    ref var lHashSystem = ref pState.WorldUnmanaged.GetUnsafeSystemRef<EnemyHashSystem>(lHashHandle);
                    lHashSystem.mGridJobHandle.Complete();
                    lHashSystem.mGrid.mCount = 0;
                }
                return;
            }

            // Benchmark: horde size set from the benchmark HUD. Game: horde size driven by the WaveDirector.
            // Outside of a session (main menu), no horde
            bool lIsBenchmark = GameManager.IsBenchmarkMode;
            bool lInSession = GameManager.Instance != null && GameManager.Instance.IsInSession;
            int lTargetEnemyCount = !lInSession ? 0
                : lIsBenchmark ? BenchmarkController.TargetEnemyCount
                : WaveDirector.TargetEnemyCount;
            int lCurrentEnemyCount = mEnemyQuery.CalculateEntityCount();

            // Clear immediately if target is 0
            if (lTargetEnemyCount == 0)
            {
                // Enemies already flagged dead are left to the EnemyDeathSystem
                pState.EntityManager.DestroyEntity(mAliveEnemyQuery);
                pState.EntityManager.DestroyEntity(mExperienceQuery);
                EnemyCounterSystem.TotalEnemies = 0;
                EnemyCounterSystem.VisibleEnemies = 0;
                return;
            }

            // --- Dynamic Despawn / Excess Trimming (benchmark only) ---
            // In Game mode the target never decreases: the horde only exceeds it when the boss spawns into a full
            // horde, and trimming would then destroy the boss (or make regular enemies vanish on screen)
            if (lIsBenchmark && lCurrentEnemyCount > lTargetEnemyCount)
            {
                int lExcess = lCurrentEnemyCount - lTargetEnemyCount;
                using var lEntities = mAliveEnemyQuery.ToEntityArray(Allocator.Temp);
                int lToDestroy = math.min(lExcess, lEntities.Length);
                for (int i = 0; i < lToDestroy; i++)
                {
                    pState.EntityManager.DestroyEntity(lEntities[i]);
                }
                EnemyCounterSystem.TotalEnemies = lTargetEnemyCount;
                return;
            }

            // Benchmark: nothing to do once the horde is full.
            // Game: the boss, elites and runner packs may still spawn when the horde is full
            if (lIsBenchmark && lCurrentEnemyCount >= lTargetEnemyCount) return;

            // --- Camera Initialization Check ---
            // Ensure main camera exists before proceeding with spawn logic
            if (Camera.main == null)
            {
                Debug.LogWarning("Spawner: No camera tagged 'MainCamera' found in the scene!");
                return;
            }

            // --- One-Time Mesh and Material Registration ---
            // Register the mesh and material of each enemy prefab once, then assign them directly to spawned enemies
            if (!mInitialized)
            {
                foreach (var lSpawnerData in SystemAPI.Query<RefRO<EnemySpawnerData>>())
                {
                    if (!TryRegisterRendering(ref pState, lSpawnerData.ValueRO.mPrefab, out mMeshID, out mMaterialID))
                        continue;

                    // The elite and runner prefabs are optional
                    mHasElite = TryRegisterRendering(ref pState, lSpawnerData.ValueRO.mElitePrefab,
                        out mEliteMeshID, out mEliteMaterialID);
                    mHasRunner = TryRegisterRendering(ref pState, lSpawnerData.ValueRO.mRunnerPrefab,
                        out mRunnerMeshID, out mRunnerMaterialID);
                    mHasBoss = TryRegisterRendering(ref pState, lSpawnerData.ValueRO.mBossPrefab,
                        out mBossMeshID, out mBossMaterialID);

                    // Without a boss, surviving the run wins the game
                    WaveDirector.HasBossPrefab = mHasBoss;
                    mRegisteredPrefab = lSpawnerData.ValueRO.mPrefab;
                    mInitialized = true;
                }

                // Exit if initialization was incomplete
                if (!mInitialized) return;
            }

            // --- Spawn Logic ---
            // Refresh current enemy count for capacity checks
            lCurrentEnemyCount = mEnemyQuery.CalculateEntityCount();

            // Get main camera and viewport information
            Camera lCam = Camera.main;
            float3 lCamPos = lCam.transform.position;
            float lHeight = lCam.orthographicSize;
            float lWidth = lHeight * lCam.aspect;
            float lMargin = 2f;  // Buffer distance outside viewport for spawn edge

            // Game: enemies get tougher over the run. Benchmark: base health
            float lHealthMultiplier = lIsBenchmark ? 1f : WaveDirector.EnemyHealthMultiplier;

            // Iterate through all spawner entities
            foreach (var lSpawnerData in SystemAPI.Query<RefRW<EnemySpawnerData>>())
            {
                // --- Prefab Validation Check ---
                // Ensure enemy prefab is valid before attempting instantiation
                Entity lPrefab = lSpawnerData.ValueRO.mPrefab;
                Entity lElitePrefab = lSpawnerData.ValueRO.mElitePrefab;
                Entity lRunnerPrefab = lSpawnerData.ValueRO.mRunnerPrefab;
                Entity lBossPrefab = lSpawnerData.ValueRO.mBossPrefab;
                if (lPrefab == Entity.Null)
                {
                    Debug.LogError("Spawner: Enemy Prefab is missing or corrupted!");
                    continue;
                }

                var lRandom = lSpawnerData.ValueRO.mRandom;

                // --- Final boss (game mode only) ---
                // Spawned once when the run ends, even if the horde is full; its health is not scaled
                if (!lIsBenchmark && mHasBoss && WaveDirector.ConsumeBossSpawn())
                {
                    float3 lBossPosition = GetSpawnEdgePosition(ref lRandom, lCamPos, lWidth, lHeight, lMargin);
                    SpawnGroup(ref pState, ref lRandom, lBossPrefab, 1, lBossPosition, 0f,
                        mBossMeshID, mBossMaterialID, 1f);
                }

                // Calculate how many enemies can be spawned (respects target horde cap up to 1M)
                int lRemaining = lTargetEnemyCount - lCurrentEnemyCount;
                if (lRemaining <= 0)
                {
                    lSpawnerData.ValueRW.mRandom = lRandom;
                    continue;
                }

                // --- Elites (game mode only) ---
                // Spawned one by one, each on its own screen edge, at the rate set by the WaveDirector
                if (!lIsBenchmark && mHasElite)
                {
                    int lEliteCount = WaveDirector.ConsumeEliteBudget(lRemaining);
                    for (int i = 0; i < lEliteCount; i++)
                    {
                        float3 lElitePosition = GetSpawnEdgePosition(ref lRandom, lCamPos, lWidth, lHeight, lMargin);
                        SpawnGroup(ref pState, ref lRandom, lElitePrefab, 1, lElitePosition, 0f,
                            mEliteMeshID, mEliteMaterialID, lHealthMultiplier);
                    }
                    lRemaining -= lEliteCount;
                }

                // --- Runner packs (game mode only) ---
                // A whole pack spawns as a cluster on one screen edge, at the rate set by the WaveDirector
                if (!lIsBenchmark && mHasRunner)
                {
                    int lPackSize = WaveDirector.ConsumeRunnerPack(lRemaining);
                    if (lPackSize > 0)
                    {
                        float3 lPackPosition = GetSpawnEdgePosition(ref lRandom, lCamPos, lWidth, lHeight, lMargin);
                        SpawnGroup(ref pState, ref lRandom, lRunnerPrefab, lPackSize, lPackPosition, 1.5f,
                            mRunnerMeshID, mRunnerMaterialID, lHealthMultiplier);
                        lRemaining -= lPackSize;
                    }
                }

                // --- Regular enemies ---
                // Benchmark: fill the horde as fast as possible (5,000 per frame, 25,000 for huge gaps).
                // Game: spawn at the rate set by the WaveDirector, so the horde arrives progressively
                int lCount = lIsBenchmark
                    ? math.min(lRemaining > 50000 ? 25000 : 5000, lRemaining)
                    : WaveDirector.ConsumeSpawnBudget(lRemaining);

                if (lCount > 0)
                {
                    // The whole group spawns as a cluster on one screen edge
                    float3 lClusterCenter = GetSpawnEdgePosition(ref lRandom, lCamPos, lWidth, lHeight, lMargin);
                    SpawnGroup(ref pState, ref lRandom, lPrefab, lCount, lClusterCenter, 1f,
                        mMeshID, mMaterialID, lHealthMultiplier);
                }

                lSpawnerData.ValueRW.mRandom = lRandom;
            }
        }

        /// <summary>
        /// Registers the mesh and material of a prefab with the Entities Graphics system.
        /// </summary>
        /// <param name="pPrefab">The prefab entity, which may be Entity.Null.</param>
        /// <param name="pMeshID">Registered mesh ID.</param>
        /// <param name="pMaterialID">Registered material ID.</param>
        /// <returns>False if the prefab is missing or has no rendering components.</returns>
        private static bool TryRegisterRendering(ref SystemState pState, Entity pPrefab,
            out BatchMeshID pMeshID, out BatchMaterialID pMaterialID)
        {
            pMeshID = default;
            pMaterialID = default;

            var lEntityManager = pState.EntityManager;
            if (pPrefab == Entity.Null
                || !lEntityManager.HasComponent<RenderMeshArray>(pPrefab)
                || !lEntityManager.HasComponent<MaterialMeshInfo>(pPrefab)) return false;

            // Baking merges the meshes and materials of the whole subscene into a single shared RenderMeshArray:
            // the prefab's MaterialMeshInfo gives the index of its own mesh and material in that array
            var lRenderMeshArray = lEntityManager.GetSharedComponentManaged<RenderMeshArray>(pPrefab);
            var lMaterialMeshInfo = lEntityManager.GetComponentData<MaterialMeshInfo>(pPrefab);

            // Register the prefab's mesh and material with the batch renderer
            var lEntitiesGraphicsSystem = pState.World.GetExistingSystemManaged<EntitiesGraphicsSystem>();
            pMeshID = lEntitiesGraphicsSystem.RegisterMesh(lRenderMeshArray.GetMesh(lMaterialMeshInfo));
            pMaterialID = lEntitiesGraphicsSystem.RegisterMaterial(lRenderMeshArray.GetMaterial(lMaterialMeshInfo));
            return true;
        }

        /// <summary>
        /// Releases the meshes and materials registered for the enemy prefabs of the previous subscene.
        /// </summary>
        private void UnregisterRendering(ref SystemState pState)
        {
            var lEntitiesGraphicsSystem = pState.World.GetExistingSystemManaged<EntitiesGraphicsSystem>();
            if (lEntitiesGraphicsSystem == null) return;

            lEntitiesGraphicsSystem.UnregisterMesh(mMeshID);
            lEntitiesGraphicsSystem.UnregisterMaterial(mMaterialID);
            if (mHasElite)
            {
                lEntitiesGraphicsSystem.UnregisterMesh(mEliteMeshID);
                lEntitiesGraphicsSystem.UnregisterMaterial(mEliteMaterialID);
            }
            if (mHasRunner)
            {
                lEntitiesGraphicsSystem.UnregisterMesh(mRunnerMeshID);
                lEntitiesGraphicsSystem.UnregisterMaterial(mRunnerMaterialID);
            }
            if (mHasBoss)
            {
                lEntitiesGraphicsSystem.UnregisterMesh(mBossMeshID);
                lEntitiesGraphicsSystem.UnregisterMaterial(mBossMaterialID);
            }
        }

        /// <summary>
        /// Picks a random position just outside one of the four screen edges.
        /// </summary>
        /// <param name="pRandom">Random generator of the spawner.</param>
        /// <param name="pCameraPosition">World position of the camera.</param>
        /// <param name="pHalfWidth">Half of the visible width, in world units.</param>
        /// <param name="pHalfHeight">Half of the visible height, in world units.</param>
        /// <param name="pMargin">Distance outside the screen edge.</param>
        /// <returns>The spawn position, with Z at 0.</returns>
        private static float3 GetSpawnEdgePosition(ref Unity.Mathematics.Random pRandom, float3 pCameraPosition,
            float pHalfWidth, float pHalfHeight, float pMargin)
        {
            float lSpawnX = 0f, lSpawnY = 0f;

            switch (pRandom.NextInt(0, 4))
            {
                case 0: // UP - Spawn along top edge
                    lSpawnX = pRandom.NextFloat(-pHalfWidth, pHalfWidth);
                    lSpawnY = pHalfHeight + pMargin;
                    break;
                case 1: // DOWN - Spawn along bottom edge
                    lSpawnX = pRandom.NextFloat(-pHalfWidth, pHalfWidth);
                    lSpawnY = -pHalfHeight - pMargin;
                    break;
                case 2: // LEFT - Spawn along left edge
                    lSpawnX = -pHalfWidth - pMargin;
                    lSpawnY = pRandom.NextFloat(-pHalfHeight, pHalfHeight);
                    break;
                case 3: // RIGHT - Spawn along right edge
                    lSpawnX = pHalfWidth + pMargin;
                    lSpawnY = pRandom.NextFloat(-pHalfHeight, pHalfHeight);
                    break;
            }

            return new float3(pCameraPosition.x + lSpawnX, pCameraPosition.y + lSpawnY, 0f);
        }

        /// <summary>
        /// Instantiates a group of enemies around a position, keeping the prefab scale,
        /// and scaling their health with the progression of the run.
        /// </summary>
        /// <param name="pRandom">Random generator of the spawner.</param>
        /// <param name="pPrefab">Enemy prefab to instantiate.</param>
        /// <param name="pCount">Number of enemies to spawn.</param>
        /// <param name="pCenter">Center of the group.</param>
        /// <param name="pSpread">Maximum random offset from the center, to avoid perfectly overlapping enemies.</param>
        /// <param name="pMeshID">Registered mesh of the prefab.</param>
        /// <param name="pMaterialID">Registered material of the prefab.</param>
        /// <param name="pHealthMultiplier">Multiplier applied to the prefab's health.</param>
        private static void SpawnGroup(ref SystemState pState, ref Unity.Mathematics.Random pRandom, Entity pPrefab,
            int pCount, float3 pCenter, float pSpread, BatchMeshID pMeshID, BatchMaterialID pMaterialID,
            float pHealthMultiplier)
        {
            var lEntityManager = pState.EntityManager;
            float lScale = lEntityManager.GetComponentData<LocalTransform>(pPrefab).Scale;
            float lHealth = lEntityManager.GetComponentData<EnemyHealthComponent>(pPrefab).mValue * pHealthMultiplier;

            // Instantiate the whole group at once
            var lSpawnedEnemies = new NativeArray<Entity>(pCount, Allocator.Temp);
            lEntityManager.Instantiate(pPrefab, lSpawnedEnemies);

            for (int i = 0; i < lSpawnedEnemies.Length; i++)
            {
                var lEnemyEntity = lSpawnedEnemies[i];
                float3 lOffset = new float3(pRandom.NextFloat(-pSpread, pSpread), pRandom.NextFloat(-pSpread, pSpread), 0f);

                lEntityManager.SetComponentData(lEnemyEntity,
                    LocalTransform.FromPositionRotationScale(pCenter + lOffset, quaternion.identity, lScale));
                lEntityManager.SetComponentData(lEnemyEntity, new EnemyHealthComponent { mValue = lHealth });

                // Assign batch rendering information for visual rendering
                lEntityManager.SetComponentData(lEnemyEntity, new MaterialMeshInfo
                {
                    MeshID = pMeshID,
                    MaterialID = pMaterialID
                });
            }

            lSpawnedEnemies.Dispose();
        }
    }
}
