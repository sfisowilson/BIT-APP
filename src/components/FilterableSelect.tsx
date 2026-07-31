import React, { useState, useRef, useEffect, useCallback } from 'react';
import { Search, ChevronDown, X } from 'lucide-react';

interface FilterableSelectProps {
  value: string;
  onChange: (value: string) => void;
  options: readonly string[];
  placeholder?: string;
  className?: string;
  required?: boolean;
}

export const FilterableSelect: React.FC<FilterableSelectProps> = ({
  value,
  onChange,
  options,
  placeholder = 'Search...',
  className = '',
  required = false,
}) => {
  const [isOpen, setIsOpen] = useState(false);
  const [filter, setFilter] = useState('');
  const [highlightIndex, setHighlightIndex] = useState(-1);
  const wrapperRef = useRef<HTMLDivElement>(null);
  const inputRef = useRef<HTMLInputElement>(null);
  const listRef = useRef<HTMLUListElement>(null);

  const filteredOptions = filter
    ? options.filter(o => o.toLowerCase().includes(filter.toLowerCase()))
    : options;

  // Close on outside click
  useEffect(() => {
    const handleClickOutside = (e: MouseEvent) => {
      if (wrapperRef.current && !wrapperRef.current.contains(e.target as Node)) {
        setIsOpen(false);
      }
    };
    document.addEventListener('mousedown', handleClickOutside);
    return () => document.removeEventListener('mousedown', handleClickOutside);
  }, []);

  // Reset highlight when filter changes
  useEffect(() => {
    setHighlightIndex(-1);
  }, [filter]);

  // Scroll highlighted item into view
  useEffect(() => {
    if (highlightIndex >= 0 && listRef.current) {
      const item = listRef.current.children[highlightIndex] as HTMLElement | undefined;
      if (item) item.scrollIntoView({ block: 'nearest' });
    }
  }, [highlightIndex]);

  const selectOption = useCallback((option: string) => {
    onChange(option);
    setFilter('');
    setIsOpen(false);
    setHighlightIndex(-1);
  }, [onChange]);

  const handleKeyDown = (e: React.KeyboardEvent<HTMLInputElement>) => {
    if (!isOpen) {
      if (e.key === 'ArrowDown' || e.key === 'Enter') {
        setIsOpen(true);
        e.preventDefault();
      }
      return;
    }

    switch (e.key) {
      case 'ArrowDown':
        e.preventDefault();
        setHighlightIndex(prev => Math.min(prev + 1, filteredOptions.length - 1));
        break;
      case 'ArrowUp':
        e.preventDefault();
        setHighlightIndex(prev => Math.max(prev - 1, -1));
        break;
      case 'Enter':
        e.preventDefault();
        if (highlightIndex >= 0 && highlightIndex < filteredOptions.length) {
          selectOption(filteredOptions[highlightIndex]);
        }
        break;
      case 'Escape':
        setIsOpen(false);
        setFilter('');
        break;
      case 'Tab':
        setIsOpen(false);
        break;
    }
  };

  const clearValue = () => {
    onChange('');
    setFilter('');
    inputRef.current?.focus();
  };

  return (
    <div ref={wrapperRef} className={`relative ${className}`}>
      <div className="relative">
        {value ? (
          /* Selected value display */
          <div
            className="w-full bg-blue-50 border border-blue-200 rounded-lg pl-3 pr-16 py-1.5 text-xs text-blue-800 flex items-center cursor-pointer"
            onClick={() => { setIsOpen(true); setTimeout(() => inputRef.current?.focus(), 50); }}
          >
            <span className="truncate" title={value}>{value}</span>
            <button
              type="button"
              onClick={(e) => { e.stopPropagation(); clearValue(); }}
              className="absolute right-7 top-1/2 -translate-y-1/2 p-0.5 rounded text-blue-400 hover:text-red-500 hover:bg-red-50 cursor-pointer"
            >
              <X className="h-3 w-3" />
            </button>
            <ChevronDown className="absolute right-2 top-1/2 -translate-y-1/2 h-3.5 w-3.5 text-blue-400 pointer-events-none" />
          </div>
        ) : (
          /* Search input */
          <div className="relative">
            <Search className="absolute left-2.5 top-1/2 -translate-y-1/2 h-3 w-3 text-slate-400 pointer-events-none" />
            <input
              ref={inputRef}
              type="text"
              value={filter}
              onChange={(e) => { setFilter(e.target.value); setIsOpen(true); }}
              onFocus={() => setIsOpen(true)}
              onKeyDown={handleKeyDown}
              placeholder={placeholder}
              className="w-full bg-slate-50 border border-slate-200 rounded-lg pl-7 pr-8 py-1.5 text-xs text-slate-800 focus:bg-white focus:outline-none focus:border-blue-500 transition-colors"
              required={required}
            />
            <ChevronDown
              className={`absolute right-2.5 top-1/2 -translate-y-1/2 h-3.5 w-3.5 text-slate-400 transition-transform cursor-pointer ${isOpen ? 'rotate-180' : ''}`}
              onClick={() => { setIsOpen(!isOpen); if (!isOpen) inputRef.current?.focus(); }}
            />
          </div>
        )}
      </div>

      {/* Dropdown */}
      {isOpen && (
        <ul
          ref={listRef}
          className="absolute z-50 mt-1 w-full bg-white border border-slate-200 rounded-lg shadow-lg max-h-48 overflow-y-auto py-1"
        >
          {filteredOptions.length === 0 ? (
            <li className="px-3 py-2 text-[10px] text-slate-400 text-center">No categories match "{filter}"</li>
          ) : (
            filteredOptions.map((option, idx) => (
              <li
                key={option}
                onClick={() => selectOption(option)}
                onMouseEnter={() => setHighlightIndex(idx)}
                className={`px-3 py-1.5 text-xs cursor-pointer transition-colors ${
                  idx === highlightIndex
                    ? 'bg-blue-50 text-blue-700'
                    : 'text-slate-700 hover:bg-slate-50'
                } ${option === value ? 'font-bold' : ''}`}
              >
                {option}
              </li>
            ))
          )}
        </ul>
      )}
    </div>
  );
};
