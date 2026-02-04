using System.Collections.Generic;
using System.Linq;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using Unity.VisualScripting;
using Unity.Netcode;
using UnityEngine;
using StbRectPackSharp;
using KaimiraGames;
using BepInEx.Configuration;

namespace ShoppingList
{
    [BepInPlugin("yasenfire.shoppinglist", "ShoppingList", "0.1.0")]
    public class Plugin : BaseUnityPlugin
    {
        internal static new ManualLogSource Logger;
        private Harmony m_harmony = new Harmony("yasenfire.shoppinglist");

        public static WeightedList<string> weightedBuyerList;

        // ========== 添加配置项 ==========
        public static ConfigEntry<int> ConfigObsessiveWeight;
        public static ConfigEntry<int> ConfigLoyalWeight;
        public static ConfigEntry<int> ConfigStandardWeight;
        public static ConfigEntry<float> ConfigRequiredRatio;
        public static ConfigEntry<int> ConfigMaxItemsPerCustomer;
        // ===============================

        // ========== 运行时配置值（游戏开始时加载） ==========
        public static int ObsessiveWeight;
        public static int LoyalWeight;
        public static int StandardWeight;
        public static float RequiredRatio;
        public static int MaxItemsPerCustomer;
        // =================================================== 

        private void Awake()
        {
            // Plugin startup logic
            Logger = base.Logger;
            Logger.LogInfo($"Plugin ShoppingList is loaded!");

            // ========== 初始化配置 ==========
            InitConfig();
            LoadConfigValues();
            // ===============================

            this.m_harmony.PatchAll();

            // 初始化顾客类型列表（现在使用配置值）
            UpdateWeightedBuyerList();
            // ===============================

            /*List<WeightedListItem<string>> weightedBuyerTypes = new()
                {
                    new WeightedListItem<string>("obsessive", 1),
                    new WeightedListItem<string>("loyal", 20),
                    new WeightedListItem<string>("standard", 79),
                };

            weightedBuyerList = new(weightedBuyerTypes);*/
        }

        // ========== 配置初始化方法 ==========
        private void InitConfig()
        {
            ConfigObsessiveWeight = Config.Bind(
                "顾客类型",                     // 配置节
                "ObsessiveWeight",              // 配置键
                1,                              // 默认值
                "痴迷型顾客权重 (1%)"           // 描述
            );

            ConfigLoyalWeight = Config.Bind(
                "顾客类型",
                "LoyalWeight",
                20,
                "忠诚型顾客权重 (20%)"
            );

            ConfigStandardWeight = Config.Bind(
                "顾客类型",
                "StandardWeight",
                79,
                "标准型顾客权重 (79%)"
            );

            ConfigRequiredRatio = Config.Bind(
                "购物条件",
                "RequiredRatio",
                0.5f,
                new ConfigDescription(
                    "标准顾客离开阈值比例 (0.0-1.0)",
                    new AcceptableValueRange<float>(0.1f, 1.0f)  // 允许的范围
                )
            );

            ConfigMaxItemsPerCustomer = Config.Bind(
                "购物条件",
                "MaxItemsPerCustomer",
                24,
                new ConfigDescription(
                    "每个顾客最大购买物品数量",
                    new AcceptableValueRange<int>(6, 50)  // 允许的范围
                )
            );
        }
        // ==========================================

        // ========== 加载配置值到静态变量 ==========
        private void LoadConfigValues()
        {
            // 确保权重至少为1
            ObsessiveWeight = Mathf.Max(1, ConfigObsessiveWeight.Value);
            LoyalWeight = Mathf.Max(1, ConfigLoyalWeight.Value);
            StandardWeight = Mathf.Max(1, ConfigStandardWeight.Value);
            
            // 确保比例在有效范围内
            RequiredRatio = Mathf.Clamp(ConfigRequiredRatio.Value, 0.1f, 1.0f);
            
            // 确保最大购买数在6-50范围内
            MaxItemsPerCustomer = Mathf.Clamp(ConfigMaxItemsPerCustomer.Value, 6, 50);
            
            Logger.LogInfo($"配置加载完成:");
            Logger.LogInfo($"  顾客权重: 痴迷={ObsessiveWeight}, 忠诚={LoyalWeight}, 标准={StandardWeight}");
            Logger.LogInfo($"  离开阈值: {RequiredRatio:P0}");
            Logger.LogInfo($"  最大购买数: {MaxItemsPerCustomer}");
        }
        // ==========================================
        
        // ========== 更新加权列表方法 ==========
        public static void UpdateWeightedBuyerList()
        {
            List<WeightedListItem<string>> weightedBuyerTypes = new()
            {
                new WeightedListItem<string>("obsessive", ObsessiveWeight),
                new WeightedListItem<string>("loyal", LoyalWeight),
                new WeightedListItem<string>("standard", StandardWeight),
            };

            weightedBuyerList = new(weightedBuyerTypes);
        }
        // =====================================

        [HarmonyPatch(typeof(CustomerController), nameof(CustomerController.SetupShoppingList))]
        class SetupShoppingListPatch
        {
            static bool Prefix(CustomerController __instance)
            {
                __instance.shoppingList.Clear();

                List<ProductSO> list = (from x in GameManager.Instance.itemDatabase
                                        where x is ProductSO
                                        select x).Cast<ProductSO>().ToList();

                List<WeightedListItem<ProductSO>> weightedProducts = new List<WeightedListItem<ProductSO>>();
                SeasonSO currentSeason = GameManager.Instance.GetCurrentSeason();
                EventSO currentEvent = GameManager.Instance.GetCurrentEvent();

                foreach (ProductSO item in list)
                {
                    float weight = 100f;
                    if (currentSeason == item.season) weight *= 1.2f;
                    else weight *= 0.9f;

                    float sellingPrice = GameManager.Instance.GetSellingPriceServer(item);
                    float recommendedPrice = GameManager.Instance.GetRecommendedPrice(item);

                    float sellingRecommendedRatio = recommendedPrice / sellingPrice;
                    if (sellingRecommendedRatio > 1f) weight *= Mathf.Lerp(1, 8, Mathf.Clamp(sellingRecommendedRatio, 1f, 2f) - 1f);
                    else weight *= Mathf.Lerp(0.1f, 1f, (Mathf.Clamp(sellingRecommendedRatio, 0.8f, 1f) - 0.8f) * 10f);

                    weight *= Random.Range(0.8f, 1.2f);

                    if (currentEvent is not null)
                    {
                        if (currentEvent.forbiddenProducts.Contains(item)) weight *= 0.1f;
                        if (currentEvent.products.Contains(item)) weight *= Random.Range(2.0f, 4f);
                    }

                    weightedProducts.Add(new WeightedListItem<ProductSO>(item, Mathf.RoundToInt(weight)));
                }

                WeightedList<ProductSO> weightedList = new WeightedList<ProductSO>(weightedProducts);

                List<ProductSO> list2 = (from x in (from x in FindObjectsOfType<Item>()
                                                    where x.itemSO is ProductSO && x.onStand.Value && x.amount.Value > 0
                                                    select x).DistinctBy((Item x) => x.itemSO.id)
                                         select x.itemSO).Cast<ProductSO>().ToList();

                float allMarketRatio = (float)list2.Count / (float)list.Count;

                string buyerType = weightedBuyerList.Next();

                // ========== 使用配置的最大购买数 ==========
                int maxItems = MaxItemsPerCustomer;
                // =======================================

                if (buyerType == "obsessive")
                {
                    ProductSO item = weightedList.Next();

                    int num = Random.Range(4, Mathf.Min(item.amount, maxItems));
                    for (int i = 0; i < num; i += 1)
                    {
                        __instance.shoppingList.Add(item);
                    }
                    return false;
                }

                if (buyerType == "loyal")
                {
                    int maxLoyalItems = Mathf.RoundToInt(Mathf.Lerp(0, maxItems - 6, allMarketRatio)) + 6;
                    int num = Random.Range(1, maxLoyalItems);

                    for (int i = 0; i < num; i += 1)
                    {
                        ProductSO item;
                        do
                        {
                            item = weightedList.Next();
                        } while (!list2.Contains(item));
                        __instance.shoppingList.Add(item);
                    }

                    return false;
                }

                int standardNum = Random.Range(1, maxItems);
                int itemsInMarket = 0;
                List<ProductSO> buyList = new List<ProductSO>();
                for (int i = 0; i < standardNum; i += 1)
                {
                    ProductSO item = weightedList.Next();
                    buyList.Add(item);

                    int increment = 1;

                    while (i < standardNum && Random.Range(0f, 1f) < item.amount / 100 / increment)
                    {
                        buyList.Add(item);
                        increment += 1;
                        i += 1;
                    }

                    if (list2.Contains(item)) itemsInMarket += increment;
                }

                // ========== 使用配置的阈值比例 ==========
                
                float ratio = (float)itemsInMarket / standardNum;
                                
                if (ratio >= RequiredRatio)
                {
                    for (int i = 0; i < buyList.Count; i += 1)
                    {
                        if (!list2.Contains(buyList[i])) continue;
                        __instance.shoppingList.Add(buyList[i]);
                    }
                }

                return false;
            }
        }

        [HarmonyPatch(typeof(Checkout), nameof(Checkout.SpawnProducts))]
        class CheckoutSpawnProductsPatch
        {
            static bool Prefix(Checkout __instance)
            {
                if (__instance.spawnedProducts)
                {
                    return false;
                }

                CheckoutEmployee employee = Traverse.Create(__instance).Field("checkoutEmployee").GetValue() as CheckoutEmployee;
                List<CustomerController> customers = Traverse.Create(__instance).Field("customers").GetValue() as List<CustomerController>;

                Transform initialSlot = __instance.productSlots.GetChild(0);
                Transform lastSlot = __instance.productSlots.GetChild(8);

                float areaWidth = Mathf.Abs(lastSlot.localPosition.x * 1000 - initialSlot.localPosition.x * 1000);
                float areaHeight = Mathf.Abs(lastSlot.localPosition.z * 1000 - initialSlot.localPosition.z * 1000) * 2;

                List<ProductSO> shoppingCart = customers[0].shoppingCart;
                shoppingCart.Sort((x, y) => x.id.CompareTo(y.id));

                Packer packer = new Packer(Mathf.RoundToInt(areaWidth), Mathf.RoundToInt(areaHeight));

                for (int i = 0; i < shoppingCart.Count; i += 1)
                {
                    Transform child = __instance.productSlots.GetChild(0);
                    ProductSO productSO = shoppingCart[i];
                    GameObject newItem = Instantiate(GameManager.Instance.checkoutItem, child.position, child.rotation);
                    Mesh renderer = productSO.pickupPrefab.GetComponentsInChildren<MeshFilter>()[0].sharedMesh;
                    Vector3 itemBounds = renderer.bounds.size;
                    packer.PackRect(Mathf.RoundToInt(itemBounds.z * 1000 * productSO.pickupPrefab.transform.localScale.z), Mathf.RoundToInt(itemBounds.x * 1000 * productSO.pickupPrefab.transform.localScale.x), (newItem, productSO.id));
                }

                foreach (PackerRectangle packRect in packer.PackRectangles)
                {
                    (GameObject item, long id) = ((GameObject, long))packRect.Data;
                    NetworkObject component = item.GetComponent<NetworkObject>();
                    component.Spawn(false);
                    component.GetComponent<CheckoutItem>().ServerSetupItem(id, __instance);
                    component.TrySetParent(__instance.gameObject.transform, true);
                    item.transform.localPosition += new Vector3((float)packRect.Rectangle.Y / 1000, 0f, (float)packRect.Rectangle.X / 1000 * -1);
                }
                __instance.spawnedProducts = true;
                if (employee != null)
                {
                    employee.Work();
                }
                return false;
            }
        }
    }
}
