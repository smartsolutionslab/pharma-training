/** A rate as the matrix shows it: the value to format and the number format that shows it. */
export interface RateFigure {
  value: number;
  format: 'percent' | 'percentTenths';
}

/**
 * The matrix's compliance rate as a figure (#276): whole percent, except just below everyone and just above nobody,
 * where one decimal is shown, rounded towards the middle. So "100 %" means everyone is compliant and "0 %" nobody.
 */
export function rateFigure(rate: number): RateFigure {
  if (rate > 0.99 && rate < 1) {
    return { value: Math.floor(rate * 1000) / 1000, format: 'percentTenths' };
  }
  if (rate > 0 && rate < 0.01) {
    return { value: Math.ceil(rate * 1000) / 1000, format: 'percentTenths' };
  }
  return { value: rate, format: 'percent' };
}
