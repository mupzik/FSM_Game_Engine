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
    /// Представляет игровой предмет с характеристиками
    /// </summary>
    public class Item
    {
        // Уникальный идентификатор предмета
        public string Key { get; private set; }
        // Отображаемое имя предмета
        public string Name { get; private set; }
        // Описание предмета
        public string Description { get; private set; }
        // Путь к иконке предмета
        public string IconPath { get; private set; }
        // Можно ли складывать предметы в стопку
        public bool IsStackable { get; private set; }
        // Текущее количество предметов в стопке
        public int Quantity { get; private set; }

        /// <summary>
        /// Конструктор предмета
        /// </summary>
        /// <param name="key">Уникальный ключ предмета</param>
        /// <param name="name">Название предмета</param>
        /// <param name="description">Описание</param>
        /// <param name="iconPath">Путь к иконке</param>
        /// <param name="isStackable">Возможность складывания</param>
        /// <param name="quantity">Начальное количество (по умолчанию 1)</param>
        public Item(string key, string name, string description,
                   string iconPath, bool isStackable, int quantity = 1)
        {
            // Проверка обязательных полей
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
        /// Изменяет количество предмета
        /// </summary>
        /// <param name="amount">На сколько изменить (может быть отрицательным)</param>
        /// <returns>Новое количество или 0 если предмет закончился</returns>
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
        /// Создает полную копию предмета
        /// </summary>
        public Item Clone()
        {
            return new Item(Key, Name, Description, IconPath, IsStackable, Quantity);
        }

        /// <summary>
        /// Проверяет, можно ли объединить этот предмет с другим в одну стопку
        /// </summary>
        public bool CanStackWith(Item other)
        {
            if (other == null) return false;
            // Оба должны быть стакаемыми и иметь одинаковый ключ
            return IsStackable && other.IsStackable && Key == other.Key;
        }

        /// <summary>
        /// Текстовое представление предмета (например "Зелье x5")
        /// </summary>
        public override string ToString()
        {
            return string.Format("{0} x{1}", Name, Quantity);
        }
    }

    /// <summary>
    /// Система инвентаря с использованием словаря для быстрого доступа
    /// </summary>
    public class Inventory
    {
        // Событие при изменении предметов в ячейках
        public event Action<IEnumerable<int>> ItemsChanged;

        // Хранилище предметов: ключ - ID предмета, значение - список предметов в разных слотах
        private Dictionary<string, List<StoredItem>> _items;

        // Для быстрого доступа к предмету по слоту
        private Dictionary<int, StoredItem> _slotMap;

        // Размеры сетки инвентаря
        public int Columns { get; private set; }
        public int Rows { get; private set; }
        public int TotalSlots { get { return Columns * Rows; } }

        // Следующий свободный слот
        private int _nextFreeSlot;

        // Вспомогательный класс для хранения предмета в слоте
        private class StoredItem
        {
            public Item Item { get; set; }
            public int Slot { get; set; }
        }

        /// <summary>
        /// Создает инвентарь с указанными размерами
        /// </summary>
        public Inventory(int columns = 5, int rows = 5)
        {
            if (columns <= 0 || rows <= 0)
                throw new ArgumentOutOfRangeException("Dimensions must be positive");

            Columns = columns;
            Rows = rows;
            _items = new Dictionary<string, List<StoredItem>>();
            _slotMap = new Dictionary<int, StoredItem>();
            _nextFreeSlot = 0;
        }

        /// <summary>
        /// Получает предмет из ячейки по индексу
        /// </summary>
        public Item GetItem(int index)
        {
            ValidateIndex(index);
            return _slotMap.ContainsKey(index) ? _slotMap[index].Item : null;
        }

        /// <summary>
        /// Добавляет предмет в инвентарь
        /// </summary>
        public bool AddItem(Item item, int quantity = 1)
        {
            if (item == null) throw new ArgumentNullException("item");
            if (quantity <= 0) return false;

            int remainingToAdd = quantity;

            // Для стакаемых предметов - ищем существующие стеки
            if (item.IsStackable)
            {
                if (_items.ContainsKey(item.Key))
                {
                    var stacks = _items[item.Key];
                    foreach (var storedItem in stacks)
                    {
                        int canAdd = int.MaxValue; // Можно добавить сколько угодно
                        storedItem.Item.ChangeQuantity(remainingToAdd);
                        NotifyItemsChanged(storedItem.Slot);
                        return true;
                    }
                }
            }

            // Добавляем в новые слоты
            while (remainingToAdd > 0)
            {
                if (_nextFreeSlot >= TotalSlots) return false; // Нет свободных слотов

                int stackSize = item.IsStackable ? remainingToAdd : 1;
                Item newItem = item.Clone();
                if (item.IsStackable)
                {
                    newItem.ChangeQuantity(stackSize - 1);
                }

                var storedItem = new StoredItem
                {
                    Item = newItem,
                    Slot = _nextFreeSlot
                };

                _slotMap[_nextFreeSlot] = storedItem;

                if (!_items.ContainsKey(item.Key))
                    _items[item.Key] = new List<StoredItem>();
                _items[item.Key].Add(storedItem);

                NotifyItemsChanged(_nextFreeSlot);

                remainingToAdd -= stackSize;
                _nextFreeSlot++;
            }

            return true;
        }

        /// <summary>
        /// Удаляет указанное количество предметов
        /// </summary>
        public bool RemoveItem(string itemKey, int amount = 1)
        {
            if (amount <= 0) return false;
            if (!_items.ContainsKey(itemKey)) return false;

            int remainingToRemove = amount;
            var stacks = _items[itemKey];

            for (int i = stacks.Count - 1; i >= 0 && remainingToRemove > 0; i--)
            {
                var storedItem = stacks[i];
                var item = storedItem.Item;

                if (item.Quantity <= remainingToRemove)
                {
                    // Удаляем весь стек
                    remainingToRemove -= item.Quantity;
                    _slotMap.Remove(storedItem.Slot);
                    stacks.RemoveAt(i);
                    NotifyItemsChanged(storedItem.Slot);

                    // Обновляем _nextFreeSlot если удалили последний слот
                    if (storedItem.Slot < _nextFreeSlot)
                        _nextFreeSlot = storedItem.Slot;
                }
                else
                {
                    // Уменьшаем количество
                    item.ChangeQuantity(-remainingToRemove);
                    remainingToRemove = 0;
                    NotifyItemsChanged(storedItem.Slot);
                }
            }

            // Если все стеки удалены, убираем ключ из словаря
            if (_items[itemKey].Count == 0)
                _items.Remove(itemKey);

            return remainingToRemove == 0;
        }

        /// <summary>
        /// Получает общее количество предметов
        /// </summary>
        public int GetTotalQuantity(string itemKey)
        {
            if (!_items.ContainsKey(itemKey)) return 0;

            int total = 0;
            foreach (var storedItem in _items[itemKey])
            {
                total += storedItem.Item.Quantity;
            }
            return total;
        }

        /// <summary>
        /// Проверяет наличие предмета
        /// </summary>
        public bool HasItem(string itemKey)
        {
            return _items.ContainsKey(itemKey) && _items[itemKey].Count > 0;
        }

        /// <summary>
        /// Изменяет количество предмета в указанной ячейке
        /// </summary>
        public bool ModifyItemQuantity(int index, int amount)
        {
            ValidateIndex(index);
            if (!_slotMap.ContainsKey(index)) return false;

            var storedItem = _slotMap[index];
            var item = storedItem.Item;

            if (!item.IsStackable)
                throw new InvalidOperationException("Cannot modify quantity of non-stackable item");

            int newQuantity = item.ChangeQuantity(amount);

            if (newQuantity == 0)
            {
                // Удаляем предмет
                RemoveItem(item.Key, item.Quantity);
            }
            else
            {
                NotifyItemsChanged(index);
            }

            return true;
        }

        /// <summary>
        /// Очищает инвентарь
        /// </summary>
        public void Clear()
        {
            _items.Clear();
            _slotMap.Clear();
            _nextFreeSlot = 0;
            NotifyItemsChanged(-1);
        }

        // Проверяет индекс
        private void ValidateIndex(int index)
        {
            if (index < 0 || index >= TotalSlots)
                throw new ArgumentOutOfRangeException("index");
        }

        // Вызывает событие об изменении
        private void NotifyItemsChanged(int index)
        {
            ItemsChanged?.Invoke(index < 0
                ? Enumerable.Range(0, TotalSlots)
                : new int[] { index });
        }
    }

    /// <summary>
    /// DTO (Data Transfer Object) для загрузки предметов из JSON
    /// </summary>
    public class ItemDto
    {
        public string Id { get; set; }          // Уникальный ID
        public string Name { get; set; }        // Название
        public string Description { get; set; } // Описание
        public int Price { get; set; }          // Цена
        public bool IsStackable { get; set; }   // Стакаемость
        public float Weight { get; set; }       // Вес
        public string SpritePath { get; set; }  // Путь к спрайту
        public string UseActionType { get; set; } // Тип действия при использовании
        public float UseValue { get; set; }     // Значение действия
    }

    /// <summary>
    /// Загрузчик предметов из JSON файла
    /// </summary>
    public static class ItemLoader
    {
        /// <summary>
        /// Загружает список предметов из JSON файла
        /// </summary>
        /// <param name="filePath">Путь к JSON файлу</param>
        /// <returns>Список созданных предметов</returns>
        public static List<Item> LoadFromJson(string filePath)
        {
            // Проверяем существование файла
            if (!File.Exists(filePath))
                throw new FileNotFoundException($"Items config not found: {filePath}");

            // Читаем и десериализуем JSON
            var json = File.ReadAllText(filePath);
            var dtos = JsonSerializer.Deserialize<List<ItemDto>>(json);

            // Преобразуем DTO в предметы
            var items = new List<Item>();
            foreach (var dto in dtos)
            {
                var item = new Item(
                    dto.Id,                                    // ключ
                    dto.Name,                                  // название
                    dto.Description,                           // описание
                    Path.Combine(dto.SpritePath + ".png"),    // путь к иконке
                    dto.IsStackable,                           // стакаемость
                    1                                          // начальное количество
                );
                items.Add(item);
            }
            return items;
        }

        /// <summary>
        /// Создает действие, которое будет выполнено при использовании предмета
        /// </summary>
        /// <param name="dto">DTO с данными о действии</param>
        /// <returns>Функция действия над персонажем или null</returns>
        public static Action<Character> CreateUseAction(ItemDto dto)
        {
            // Лечение персонажа
            if (dto.UseActionType == "Heal")
            {
                return ch => ch.Heal(dto.UseValue);
            }
            // Нанесение урона
            else if (dto.UseActionType == "Damage")
            {
                return ch => ch.TakeDamage(dto.UseValue);
            }
            return null;
        }
    }
}