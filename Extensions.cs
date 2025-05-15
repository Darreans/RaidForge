using Il2CppInterop.Runtime;
using System;
using System.Runtime.InteropServices;
using Unity.Entities;
using ProjectM;

namespace RaidForge
{
	internal static class Extensions
	{
		private static EntityManager Em => VWorldUtils.EntityManager;

		public static bool Exists(this Entity entity)
		{
			return entity != Entity.Null && Em.Exists(entity);
		}
		public static bool Has<T>(this Entity entity) where T : struct
		{
			return Em.HasComponent<T>(entity);
		}
		
		public static unsafe T Read<T>(this Entity entity) where T : struct
		{
			return Em.GetComponentData<T>(entity);
		}

		public static void Write<T>(this Entity entity, T data) where T : struct
		{
			Em.SetComponentData(entity, data);
		}

		public static void Add<T>(this Entity entity) where T : struct
		{
			if (!entity.Has<T>())
			{
				Em.AddComponent<T>(entity);
			}
		}
		
		public delegate void WithRefHandler<T>(ref T item);
		public static void With<T>(this Entity entity, WithRefHandler<T> action) where T : struct
		{
			if (!entity.Exists()) return;
			var current = entity.Read<T>();
			action(ref current);
			entity.Write(current);
		}

		public static void ExploreEntity(this Entity entity)
		{
			var sb = new Il2CppSystem.Text.StringBuilder();
			ProjectM.EntityDebuggingUtility.DumpEntity(VWorldUtils.Server, entity, true, sb);
			RaidForgePlugin.Logger.LogInfo(sb.ToString());
		}
	}
}