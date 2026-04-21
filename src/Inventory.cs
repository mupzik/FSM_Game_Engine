using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace GameProj.src
{
    /// <summary>
    /// Представляет предмет в системе инвентаря.
    /// </summary>
    public class Item
    {
        // Свойства с инкапсуляцией
        public string Key { get; private set; }
        public string Name { get; private set; }
        public string Description { get; private set; }
        public string IconPath { get; private set; }
        public bool IsStackable { get; private set; }
        public int Quantity { get; private set; }

        /// <summary>
        /// Конструктор для создания нового предмета.
        /// </summary>
        public Item(string key, string name, string description,
                   string iconPath, bool isStackable, int quantity = 1)
        {
            if (string.IsNullOrEmpty(key))
                throw new ArgumentException("Key cannot be empty", "key");
            if (string.IsNullOrEmpty(name))
                throw new ArgumentException("Name cannot be empty", "name");
            if (quantity < 0)
                throw new ArgumentOutOfRangeException("quantity", "Quantity cannot be negative");

            Key = key;
            Name = name;
            Description = description;
            IconPath = iconPath;
            IsStackable = isStackable;
            Quantity = quantity;
        }

        /// <summary>
        /// Изменяет количество предмета.
        /// </summary>
        public int ChangeQuantity(int amount)
        {
            if (!IsStackable && amount != 0)
                throw new InvalidOperationException("Non-stackable items cannot change quantity");

            Quantity += amount;

            if (Quantity <= 0)
            {
                Quantity = 0;
                return 0;
            }

            return Quantity;
        }

        /// <summary>
        /// Создаёт копию предмета.
        /// </summary>
        public Item Clone()
        {
            return new Item(Key, Name, Description, IconPath, IsStackable, Quantity);
        }

        /// <summary>
        /// Проверяет, можно ли объединить данный предмет с другим.
        /// </summary>
        public bool CanStackWith(Item other)
        {
            if (other == null) return false;
            return IsStackable && other.IsStackable && Key == other.Key;
        }

        public override string ToString()
        {
            return string.Format("{0} x{1}", Name, Quantity);
        }
    }

    /// <summary>
    /// Представляет инвентарь игрока с сеткой ячеек.
    /// </summary>
    public class Inventory
    {
        public event Action<IEnumerable<int>> ItemsChanged;

        public int Columns { get; private set; }
        public int Rows { get; private set; }
        public int TotalSlots { get { return Columns * Rows; } }

        private readonly Item[] _items;

        // ✅ Исправлено: добавлены значения по умолчанию для совместимости
        public Inventory(int columns = 5, int rows = 5)
        {
            if (columns <= 0 || rows <= 0)
                throw new ArgumentOutOfRangeException("Dimensions must be positive");

            Columns = columns;
            Rows = rows;
            _items = new Item[TotalSlots];
        }

        public Item GetItem(int index)
        {
            ValidateIndex(index);
            return _items[index];
        }

        public Item SetItem(int index, Item item)
        {
            ValidateIndex(index);
            var previousItem = _items[index];
            _items[index] = item;
            NotifyItemsChanged(index);
            return previousItem;
        }

        public void RemoveItem(string itemKey)
        {
            for (int i = 0; i < TotalSlots; i++)
            {
                if (_items[i] != null && _items[i].Key == itemKey)
                {
                    _items[i] = null;
                    NotifyItemsChanged(i);
                    return;
                }
            }
        }


        public bool ModifyItemQuantity(int index, int amount)
        {
            ValidateIndex(index);
            var item = _items[index];
            if (item == null) return false;

            int newQuantity = item.ChangeQuantity(amount);
            if (newQuantity == 0) _items[index] = null;

            NotifyItemsChanged(index);
            return true;
        }

        public int AddItem(Item item)
        {
            if (item == null) throw new ArgumentNullException("item");

            if (item.IsStackable)
            {
                for (int i = 0; i < TotalSlots; i++)
                {
                    var existingItem = _items[i];
                    if (existingItem != null && existingItem.CanStackWith(item))
                    {
                        existingItem.ChangeQuantity(item.Quantity);
                        NotifyItemsChanged(i);
                        return i;
                    }
                }
            }

            for (int i = 0; i < TotalSlots; i++)
            {
                if (_items[i] == null)
                {
                    _items[i] = item.Clone();
                    NotifyItemsChanged(i);
                    return i;
                }
            }

            return -1;
        }

        public bool HasItem(string itemKey)
        {
            foreach (var item in _items)
            {
                if (item != null && item.Key == itemKey)
                    return true;
            }
            return false;
        }

        public int GetTotalQuantity(string itemKey)
        {
            int total = 0;
            foreach (var item in _items)
            {
                if (item != null && item.Key == itemKey)
                    total += item.Quantity;
            }
            return total;
        }

        public void Clear()
        {
            Array.Clear(_items, 0, _items.Length);
            NotifyItemsChanged(-1);
        }

        private void ValidateIndex(int index)
        {
            if (index < 0 || index >= TotalSlots)
                throw new ArgumentOutOfRangeException("index",
                    string.Format("Index must be between 0 and {0}", TotalSlots - 1));
        }

        private void NotifyItemsChanged(int index)
        {
            if (ItemsChanged != null)
            {
                var indexes = index < 0
                    ? (IEnumerable<int>)new int[TotalSlots]
                    : new int[] { index };
                ItemsChanged(indexes);
            }
        }
    }

    /// <summary>
    /// DTO для десериализации предмета из JSON.
    /// </summary>
    public class ItemDto
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public int Price { get; set; }
        public bool IsStackable { get; set; }
        public float Weight { get; set; }
        public string SpritePath { get; set; }
        public string UseActionType { get; set; }
        public float UseValue { get; set; }
    }

    /// <summary>
    /// Загружает предметы из JSON файла.
    /// </summary>
    public static class ItemLoader
    {
        public static List<Item> LoadFromJson(string filePath)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException($"Items config not found: {filePath}");

            var json = File.ReadAllText(filePath);
            var dtos = JsonSerializer.Deserialize<List<ItemDto>>(json);

            var items = new List<Item>();
            foreach (var dto in dtos)
            {
                var item = new Item(
                    dto.Id,                           // key
                    dto.Name,                         // name
                    dto.Description,                  // description
                    Path.Combine(dto.SpritePath + ".png"), // iconPath
                    dto.IsStackable,                  // isStackable
                    1                                 // quantity (по умолчанию)
                );
                items.Add(item);
            }
            return items;
        }

        public static Action<Character> CreateUseAction(ItemDto dto)
        {
            if (dto.UseActionType == "Heal")
            {
                return ch => ch.Heal(dto.UseValue);
            }
            else if (dto.UseActionType == "Damage")
            {
                return ch => ch.TakeDamage(dto.UseValue);
            }
            return null;
        }
    }
}