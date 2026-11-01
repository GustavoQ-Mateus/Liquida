import { useRef, useState } from 'react';
import { ConfirmPopover } from './confirm-popover';
import './clean-control.scss';

interface CleanControlProps {
  disabled: boolean;
  disabledReason: string;
  onConfirm: () => void;
}

export function CleanControl({ disabled, disabledReason, onConfirm }: CleanControlProps) {
  const [open, setOpen] = useState(false);
  const triggerRef = useRef<HTMLButtonElement>(null);

  const close = () => {
    setOpen(false);
    triggerRef.current?.focus();
  };

  return (
    <div className="clean-control">
      <button
        ref={triggerRef}
        type="button"
        className="clean-control__trigger"
        aria-label="Limpar ambiente"
        aria-expanded={open}
        aria-haspopup="dialog"
        title={disabled ? disabledReason : 'Limpar ambiente'}
        disabled={disabled}
        onClick={() => setOpen((current) => !current)}
      >
        <svg viewBox="0 0 20 20" aria-hidden="true">
          <path d="M7.5 3.5h5M4 6h12M5.5 6l.8 10a1 1 0 0 0 1 .9h5.4a1 1 0 0 0 1-.9l.8-10M8.5 9v5M11.5 9v5" />
        </svg>
      </button>
      {open ? (
        <ConfirmPopover
          onCancel={close}
          onConfirm={() => {
            setOpen(false);
            onConfirm();
          }}
        />
      ) : null}
    </div>
  );
}
