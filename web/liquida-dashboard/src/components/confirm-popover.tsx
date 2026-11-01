import { useEffect, useRef } from 'react';
import './confirm-popover.scss';

interface ConfirmPopoverProps {
  onCancel: () => void;
  onConfirm: () => void;
}

export function ConfirmPopover({ onCancel, onConfirm }: ConfirmPopoverProps) {
  const popoverRef = useRef<HTMLDivElement>(null);
  const cancelRef = useRef<HTMLButtonElement>(null);
  const onCancelRef = useRef(onCancel);
  onCancelRef.current = onCancel;

  useEffect(() => {
    cancelRef.current?.focus();
    const closeOnEscape = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        onCancelRef.current();
      }
    };
    const closeOnOutside = (event: PointerEvent) => {
      const anchor = popoverRef.current?.parentElement;
      if (anchor && !anchor.contains(event.target as Node)) {
        onCancelRef.current();
      }
    };
    document.addEventListener('keydown', closeOnEscape);
    document.addEventListener('pointerdown', closeOnOutside);
    return () => {
      document.removeEventListener('keydown', closeOnEscape);
      document.removeEventListener('pointerdown', closeOnOutside);
    };
  }, []);

  return (
    <div ref={popoverRef} className="confirm-popover" role="dialog" aria-labelledby="confirmar-limpeza">
      <p id="confirmar-limpeza" className="confirm-popover__text">
        Isso apaga todos os dados da simulação no BankCore e no Liquida.
      </p>
      <div className="confirm-popover__actions">
        <button ref={cancelRef} type="button" className="confirm-popover__cancel" onClick={onCancel}>
          Cancelar
        </button>
        <button type="button" className="confirm-popover__confirm" onClick={onConfirm}>
          Apagar dados
        </button>
      </div>
    </div>
  );
}
