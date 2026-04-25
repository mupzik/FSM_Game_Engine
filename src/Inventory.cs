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
            // Нестакаемые предметы нельзя изменять
            if (!IsStackable && amount != 0)
                throw new InvalidOperationException("Non-stackable items cannot change quantity");

            Quantity += amount;

            // Если количество стало 0 или меньше, обнуляем
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
    /// Система инвентаря с сеткой ячеек
    /// </summary>
    public class Inventory
    {
        // Событие при изменении предметов в ячейках
        public event Action<IEnumerable<int>> ItemsChanged;

        // Размеры сетки инвентаря
        public int Columns { get; private set; }
        public int Rows { get; private set; }
        // Общее количество ячеек
        public int TotalSlots { get { return Columns * Rows; } }

        // Внутренний массив предметов
        private readonly Item[] _items;

        /// <summary>
        /// Создает инвентарь с указанными размерами
        /// </summary>
        /// <param name="columns">Количество колонок (по умолчанию 5)</param>
        /// <param name="rows">Количество строк (по умолчанию 5)</param>
        public Inventory(int columns = 5, int rows = 5)
        {
            if (columns <= 0 || rows <= 0)
                throw new ArgumentOutOfRangeException("Dimensions must be positive");

            Columns = columns;
            Rows = rows;
            _items = new Item[TotalSlots];
        }

        /// <summary>
        /// Получает предмет из ячейки по индексу
        /// </summary>
        public Item GetItem(int index)
        {
            ValidateIndex(index);
            return _items[index];
        }

        /// <summary>
        /// Устанавливает предмет в ячейку
        /// </summary>
        /// <returns>Предмет, который был в ячейке ранее</returns>
        public Item SetItem(int index, Item item)
        {
            ValidateIndex(index);
            var previousItem = _items[index];
            _items[index] = item;
            NotifyItemsChanged(index);
            return previousItem;
        }

        /// <summary>
        /// Удаляет все предметы с указанным ключом (первый найденный)
        /// </summary>
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

        /// <summary>
        /// Изменяет количество предмета в указанной ячейке
        /// </summary>
        /// <returns>true если изменение успешно, false если ячейка пуста</returns>
        public bool ModifyItemQuantity(int index, int amount)
        {
            ValidateIndex(index);
            var item = _items[index];
            if (item == null) return false;

            int newQuantity = item.ChangeQuantity(amount);
            // Если предметов не осталось, очищаем ячейку
            if (newQuantity == 0) _items[index] = null;

            NotifyItemsChanged(index);
            return true;
        }

        /// <summary>
        /// Добавляет предмет в инвентарь
        /// </summary>
        /// <returns>Индекс ячейки, где оказался предмет, или -1 если места нет</returns>
        public int AddItem(Item item)
        {
            if (item == null) throw new ArgumentNullException("item");

            // Для стакаемых предметов сначала ищем существующую стопку
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

            // Ищем первую пустую ячейку
            for (int i = 0; i < TotalSlots; i++)
            {
                if (_items[i] == null)
                {
                    _items[i] = item.Clone();
                    NotifyItemsChanged(i);
                    return i;
                }
            }

            // Нет свободного места
            return -1;
        }

        /// <summary>
        /// Проверяет, есть ли предмет с указанным ключом в инвентаре
        /// </summary>
        public bool HasItem(string itemKey)
        {
            foreach (var item in _items)
            {
                if (item != null && item.Key == itemKey)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Получает общее количество предметов с указанным ключом
        /// </summary>
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

        /// <summary>
        /// Очищает весь инвентарь
        /// </summary>
        public void Clear()
        {
            Array.Clear(_items, 0, _items.Length);
            NotifyItemsChanged(-1); // -1 означает "все ячейки"
        }

        // Проверяет, что индекс находится в допустимых пределах
        private void ValidateIndex(int index)
        {
            if (index < 0 || index >= TotalSlots)
                throw new ArgumentOutOfRangeException("index",
                    string.Format("Index must be between 0 and {0}", TotalSlots - 1));
        }

        // Вызывает событие об изменении ячеек
        private void NotifyItemsChanged(int index)
        {
            if (ItemsChanged != null)
            {
                var indexes = index < 0
                    ? (IEnumerable<int>)new int[TotalSlots] // Изменились все ячейки
                    : new int[] { index }; // Изменилась одна ячейка
                ItemsChanged(indexes);
            }
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